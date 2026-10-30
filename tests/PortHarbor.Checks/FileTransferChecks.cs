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

    static void FileTransferWorkflowUi(string output)
    {
        static Button Action(FileView view, string title) => view.Controls.Find("FileAction" + title, true).OfType<Button>().Single();
        static void Pump(Func<bool> ready)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && timer.Elapsed < TimeSpan.FromSeconds(6)) { Application.DoEvents(); Thread.Sleep(10); }
            if (!ready()) throw new TimeoutException("File workflow UI fixture did not complete.");
        }
        var backend = new MemoryRemoteFileSystem();
        backend.Listing = directory => directory == "/" ? [new("folder", "/folder", true, 0, default), new("zeta.txt", "/zeta.txt", false, 1, DateTime.UnixEpoch), new("alpha.txt", "/alpha.txt", false, 2, DateTime.UnixEpoch)] : [];
        using var host = new Form { Width = 1000, Height = 680 };
        var view = new FileView(new FileConnection(Protocol.SFTP, backend), "/") { Dock = DockStyle.Fill };
        host.Controls.Add(view); host.Show();
        var listing = view.Controls.Find("RemoteEntries", true).OfType<ListView>().Single();
        var empty = view.Controls.Find("FileEmptyState", true).OfType<Label>().Single();
        var status = view.Controls.Find("FileStatus", true).OfType<Label>().Single();
        Pump(() => status.Text == "就绪" && listing.Items.Count == 3);
        Check(!Action(view, "下载").Enabled && !Action(view, "重命名").Enabled && !Action(view, "删除").Enabled
            && Action(view, "前往").Enabled && Action(view, "刷新").Enabled,
            "file toolbar enables navigation when ready and requires a selected entry for item operations");
        listing.Items[0].Selected = true; listing.Select(); Application.DoEvents();
        Check(!Action(view, "下载").Enabled && Action(view, "重命名").Enabled, "directory selection permits rename and deletion while disabling file download");
        listing.Items[0].Selected = false; listing.Items[1].Selected = true; listing.Select(); Application.DoEvents();
        Check(Action(view, "下载").Enabled && Action(view, "重命名").Enabled && Action(view, "删除").Enabled,
            "selecting an entry enables its supported file operations");
        listing.GetType().GetMethod("OnColumnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(listing, [new ColumnClickEventArgs(0)]);
        Check(listing.Items[0].Text == "folder" && listing.Items[1].Text == "zeta.txt", "clicking the active name column reverses the order while keeping directories first");
        listing.GetType().GetMethod("OnColumnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(listing, [new ColumnClickEventArgs(0)]);
        Check(listing.Items[0].Text == "folder" && listing.Items[1].Text == "alpha.txt" && listing.SelectedItems.Cast<ListViewItem>().Single().Text == "alpha.txt", "sorting back preserves the selected entry by remote path");
        Directory.CreateDirectory(output);
        using (var bitmap = new System.Drawing.Bitmap(view.Width, view.Height)) { view.DrawToBitmap(bitmap, view.ClientRectangle); bitmap.Save(Path.Combine(output, "files-list.png")); }

        Action(view, "前往").PerformClick(); Pump(() => status.Text == "就绪" && listing.Items.Count == 3);
        view.Controls.Find("RemotePath", true).OfType<TextBox>().Single().Text = "/empty";
        backend.Files.Clear(); backend.Directories.Add("/empty"); Action(view, "前往").PerformClick();
        Pump(() => status.Text == "就绪" && empty.Visible);
        Check(empty.Text == "目录为空" && listing.Items.Count == 0,
            "a successful empty listing is distinct from a failed directory load");
        backend.ListError = new IOException("private remote path failure");
        Action(view, "刷新").PerformClick(); Pump(() => status.Text.Contains("加载目录失败") && empty.Visible);
        Check(empty.Text.Contains("未能加载") && !empty.Text.Contains("private remote")
            && status.ForeColor == UiTheme.Palette(view).Error,
            "a listing failure has safe retry guidance without exposing a private exception or confusing it with empty state");
        backend.ListError = null; Action(view, "刷新").PerformClick(); Pump(() => status.Text == "就绪");
        Check(empty.Visible && empty.Text == "目录为空" && listing.Items.Count == 0, "refresh clears the prior load failure after a successful empty response");
        using (var bitmap = new System.Drawing.Bitmap(view.Width, view.Height)) { view.DrawToBitmap(bitmap, view.ClientRectangle); bitmap.Save(Path.Combine(output, "files-empty.png")); }
        CompletePickerUi(view.CloseAsync()); host.Close(); Application.DoEvents();

        var failedConnect = new MemoryRemoteFileSystem { ConnectError = new IOException("private connection path") };
        using var failedHost = new Form { Width = 900, Height = 600 };
        var failedView = new FileView(new FileConnection(Protocol.FTP, failedConnect), "/") { Dock = DockStyle.Fill };
        failedHost.Controls.Add(failedView); failedHost.Show();
        var failedStatus = failedView.Controls.Find("FileStatus", true).OfType<Label>().Single();
        Pump(() => failedStatus.Text.Contains("加载目录失败"));
        Check(failedStatus.Text.Contains("操作编号") && !failedStatus.Text.Contains("private connection path")
            && !Action(failedView, "下载").Enabled && Action(failedView, "前往").Enabled,
            "connection failure keeps its safe diagnostic visible and leaves navigation retry available");
        CompletePickerUi(failedView.CloseAsync()); failedHost.Close(); Application.DoEvents();
        FileProgressUi(output);
    }

    static void FileProgressUi(string output)
    {
        var backend = new MemoryRemoteFileSystem { UnknownLength = true, BlockReadCompletion = true };
        backend.Files["/payload.bin"] = new byte[300000];
        var picker = new FixtureFilePicker();
        string download = Path.Combine(Path.GetFullPath(output), "cancel-target.bin"); File.WriteAllText(download, "old-target"); picker.SavePath = download;
        using var host = new Form { Width = 1000, Height = 680, Font = UiTheme.BodyFont };
        var view = new FileView(new FileConnection(Protocol.SFTP, backend), "/", picker) { Dock = DockStyle.Fill };
        host.Controls.Add(view); host.Show();
        var status = view.Controls.Find("FileStatus", true).OfType<Label>().Single();
        var list = view.Controls.Find("RemoteEntries", true).OfType<ListView>().Single();
        Button Action(string name) => view.Controls.Find("FileAction" + name, true).OfType<Button>().Single();
        PumpPickerUiUntil(() => status.Text == "就绪");
        list.Items[0].Selected = true; list.Select();
        var task = view.Download();
        PumpPickerUiUntil(() => backend.ReadCompletionBlocked.Task.IsCompleted && status.Text.Contains("/秒"));
        Check(status.Text.Contains("未知大小") && !status.Text.Contains('%') && !status.Text.Contains("已完成"),
            "unknown-length download shows bytes and average speed without a fabricated percentage or premature completion");
        Check(!Action("前往").Enabled && !Action("下载").Enabled && !Action("删除").Enabled,
            "an active transfer disables navigation and item changes while retaining cancellation");
        UiTheme.Apply(host, InterfaceTheme.Dark);
        using (var bitmap = new System.Drawing.Bitmap(view.Width, view.Height)) { view.DrawToBitmap(bitmap, view.ClientRectangle); bitmap.Save(Path.Combine(output, "files-progress-dark.png")); }
        view.Controls.Find("CancelFileOperation", true).OfType<Button>().Single().PerformClick(); CompletePickerUi(task);
        Application.DoEvents();
        Check(status.Text.Contains("取消") && status.Text.Contains("核对") && File.ReadAllText(download) == "old-target"
            && backend.ReadCompletionCancelled && Action("刷新").Enabled,
            "cancelling a pending download confirmation retains the previous local target and leaves explicit refresh guidance");
        Check(!Directory.GetFiles(Path.GetDirectoryName(download)!, "*.part").Any(),
            "cancelled file-view download leaves no local staging file");
        backend.BlockReadCompletion = false; backend.UnknownLength = false;
        CompletePickerUi(view.Download()); Application.DoEvents();
        Check(status.Text == "下载已完成" && File.ReadAllBytes(download).Length == 300000,
            "download reports completion only after confirmation and late progress cannot overwrite its final result");
        string upload = Path.Combine(Path.GetFullPath(output), "draft-upload.bin"); File.WriteAllBytes(upload, [7, 8, 9]); picker.OpenPath = upload;
        view.Controls.Find("RemotePath", true).OfType<TextBox>().Single().Text = "/unvisited-draft";
        CompletePickerUi(view.Upload()); Application.DoEvents();
        Check(backend.Files.ContainsKey("/draft-upload.bin") && !backend.Files.ContainsKey("/unvisited-draft/draft-upload.bin")
            && status.Text == "上传已完成", "upload uses the confirmed listing directory rather than an unvisited path draft and preserves its final result");
        view.Controls.Find("RemotePath", true).OfType<TextBox>().Single().Text = "/new/child";
        Action("前往").PerformClick(); PumpPickerUiUntil(() => status.Text == "就绪");
        Action("上级").PerformClick(); PumpPickerUiUntil(() => status.Text == "就绪");
        Check(view.Controls.Find("RemotePath", true).OfType<TextBox>().Single().Text == "/new", "parent navigation uses the confirmed remote directory and refreshes its listing");
        CompletePickerUi(view.CloseAsync()); host.Close(); Application.DoEvents();
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
