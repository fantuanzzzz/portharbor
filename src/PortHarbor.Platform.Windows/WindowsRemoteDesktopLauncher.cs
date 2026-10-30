using System.Diagnostics;
using System.Text;

namespace PortHarbor;

public sealed class WindowsRemoteDesktopLauncher : IRemoteDesktopLauncher, IAsyncDisposable
{
    readonly Func<ProcessStartInfo, Process> startProcess;
    readonly CancellationTokenSource stop = new();
    readonly object gate = new();
    readonly List<Task> cleanups = [];
    bool disposed;
    internal Task CleanupCompletion { get { lock (gate) return Task.WhenAll(cleanups); } }

    public WindowsRemoteDesktopLauncher() : this(info => Process.Start(info) ?? throw new IOException("无法启动 Windows 远程桌面。")) { }
    internal WindowsRemoteDesktopLauncher(Func<ProcessStartInfo, Process> startProcess) => this.startProcess = startProcess;

    public static void CleanupAbandonedConfigurations(string directory) => RdpTemporaryWorkspace.Sweep(directory);

    public void Launch(string configuration, string directory, Action<ConnectionStage> reportStage)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            LaunchCore(configuration, directory, reportStage);
        }
    }

    void LaunchCore(string configuration, string directory, Action<ConnectionStage> reportStage)
    {
        RdpTemporaryWorkspace? workspace = null;
        Process? process = null;
        try
        {
            reportStage(ConnectionStage.Writing);
            workspace = RdpTemporaryWorkspace.Create(directory);
            string path = workspace.ConfigurationPath;
            File.WriteAllText(path, configuration, Encoding.Unicode);
            reportStage(ConnectionStage.LaunchingClient);
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe")) { UseShellExecute = false };
            info.ArgumentList.Add(path);
            workspace.MarkLaunching();
            process = startProcess(info);
            workspace.RecordClient(process);
            cleanups.RemoveAll(task => task.IsCompleted);
            var cleanup = CleanupAfterExit(process, workspace, stop.Token);
            cleanups.Add(cleanup);
            BackgroundTask.Observe(cleanup);
        }
        catch
        {
            process?.Dispose();
            workspace?.Dispose();
            throw;
        }
    }

    static async Task CleanupAfterExit(Process process, RdpTemporaryWorkspace workspace, CancellationToken token)
    {
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { /* Stop observing; the external client belongs to the user. */ }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); workspace.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (gate) { disposed = true; stop.Cancel(); pending = Task.WhenAll(cleanups); }
        await pending.ConfigureAwait(false);
        // No process is killed. Cancellation only releases watchers and temporary files.
    }

}
