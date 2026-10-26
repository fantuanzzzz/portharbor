using System.Diagnostics;
using System.Text;

namespace PortHarbor;

public sealed class WindowsRemoteDesktopLauncher : IRemoteDesktopLauncher
{
    readonly Func<ProcessStartInfo, Process> startProcess;

    public WindowsRemoteDesktopLauncher() : this(info => Process.Start(info) ?? throw new IOException("无法启动 Windows 远程桌面。")) { }
    internal WindowsRemoteDesktopLauncher(Func<ProcessStartInfo, Process> startProcess) => this.startProcess = startProcess;

    public void Launch(string configuration, string directory, Action<ConnectionStage> reportStage)
    {
        string? path = null;
        try
        {
            reportStage(ConnectionStage.Writing);
            path = Path.Combine(directory, "rdp-" + Guid.NewGuid().ToString("N") + ".rdp");
            File.WriteAllText(path, configuration, Encoding.Unicode);
            reportStage(ConnectionStage.LaunchingClient);
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe")) { UseShellExecute = false };
            info.ArgumentList.Add(path);
            var process = startProcess(info);
            _ = CleanupAfterExit(process, path);
        }
        catch
        {
            if (path != null) DeleteConfiguration(path);
            throw;
        }
    }

    static async Task CleanupAfterExit(Process process, string path)
    {
        try { await process.WaitForExitAsync().ConfigureAwait(false); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); DeleteConfiguration(path); }
    }

    static void DeleteConfiguration(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
