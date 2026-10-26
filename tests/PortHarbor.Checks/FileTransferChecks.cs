using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    static async Task FileTransferContract(string root)
    {
        var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "file-contract"), false));
        foreach (var protocol in Enum.GetValues<Protocol>().Where(p => p is not (Protocol.FTP or Protocol.SFTP)))
            Check(Throws<ArgumentException>(() => FileTransferFactory.Create(new Session { Protocol = protocol }, store, "", _ => false)), "file transfer factory rejects " + protocol);
        await using (var ftp = RemoteFileSystemFactory.Create(new Session { Protocol = Protocol.FTP }, store, "", _ => false))
        await using (var sftp = RemoteFileSystemFactory.Create(new Session { Protocol = Protocol.SFTP }, store, "", _ => false))
            Check(ftp is FtpRemoteFileSystem && sftp is SftpRemoteFileSystem, "FTP and SFTP select independent adapters without connecting");

        await FileTransferPolicyChecks.RunAsync(root, Check);
    }

    static void FileTransferUi()
    {
        var backend = new MemoryRemoteFileSystem();
        backend.Files["/测试文件.txt"] = [1, 2, 3];
        using var form = new Form { Width = 960, Height = 640 };
        var view = new FileView(new FileConnection(Protocol.SFTP, backend), "/") { Dock = DockStyle.Fill };
        form.Controls.Add(view); form.Show();
        var listing = view.Controls.OfType<ListView>().Single();
        var status = view.Controls.OfType<Label>().Single();
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while ((listing.Items.Count == 0 || status.Text != "就绪") && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(listing.Items.Count == 1 && listing.Items[0].Text == "测试文件.txt" && status.Text == "就绪",
            "file view loads Unicode entries through injected transfer interface");
        form.Close(); Application.DoEvents();
        Check(backend.DisposeCalls == 1, "closing file view releases its injected transfer");

        var blocked = new MemoryRemoteFileSystem { BlockList = true };
        using var closingForm = new Form { Width = 960, Height = 640 };
        var closingView = new FileView(new FileConnection(Protocol.FTP, blocked), "/") { Dock = DockStyle.Fill };
        closingForm.Controls.Add(closingView); closingForm.Show();
        var startupWait = System.Diagnostics.Stopwatch.StartNew();
        while (!blocked.ListStarted.Task.IsCompleted && startupWait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(blocked.ListStarted.Task.IsCompleted, "file view starts an async listing request");
        var closeTask = closingView.CloseAsync();
        var closeWait = System.Diagnostics.Stopwatch.StartNew();
        while (!closeTask.IsCompleted && closeWait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        closeTask.GetAwaiter().GetResult();
        Check(closeTask.IsCompleted && blocked.ListCancellationObserved && blocked.DisposeCalls == 1,
            "closing file view cancels active request and awaits asynchronous cleanup");
        closingForm.Close(); Application.DoEvents();
    }

    static void FileTransferFormCloseUi()
    {
        var backend = new MemoryRemoteFileSystem { BlockList = true };
        using var form = new MainForm(new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(Path.GetTempPath(), "PortHarbor-close-ui-" + Guid.NewGuid().ToString("N")), false)));
        form.Show();
        var tabs = FindControl<TabControl>(form);
        var page = new TabPage("async file close");
        page.Controls.Add(new FileView(new FileConnection(Protocol.SFTP, backend), "/") { Dock = DockStyle.Fill });
        tabs.TabPages.Add(page); Application.DoEvents();
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (!backend.ListStarted.Task.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(backend.ListStarted.Task.IsCompleted, "main window contains an active file request before shutdown");
        form.Close();
        wait.Restart();
        while (!form.IsDisposed && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(form.IsDisposed && backend.ListCancellationObserved && backend.DisposeCalls == 1,
            "main window shutdown waits for active file cancellation and async cleanup");
    }

}
