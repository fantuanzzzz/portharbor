using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    sealed class QueuePicker : IFilePicker, IMultiFilePicker, IFolderPicker
    {
        internal string[] Files = [];
        internal string? Folder;
        public string? OpenFile(FilePickerOptions options) => throw new Exception("unexpected single picker");
        public string? SaveFile(FilePickerOptions options) => throw new Exception("unexpected save picker");
        public IReadOnlyList<string> OpenFiles(FilePickerOptions options) => Files;
        public string? SelectFolder(string selectedPath = "") => Folder;
    }
    static void TransferQueueUi(string root, string output)
    {
        Directory.CreateDirectory(output);
        string sources = Path.Combine(root, "queue-sources"); Directory.CreateDirectory(sources);
        string first = Path.Combine(sources, "one.bin"), second = Path.Combine(sources, "two.bin");
        File.WriteAllBytes(first, [1, 2]); File.WriteAllBytes(second, [3, 4, 5]);
        var picker = new QueuePicker();
        var backend = new MemoryRemoteFileSystem();
        using var host = new Form { Width = 1200, Height = 850, Font = UiTheme.BodyFont };
        var view = new FileView(new FileConnection(Protocol.SFTP, backend), "/", picker) { Dock = DockStyle.Fill };
        host.Controls.Add(view); host.Show();
        T Control<T>(string name) where T : Control => view.Controls.Find(name, true).OfType<T>().Single();
        void Click(string name) { Control<Button>(name).PerformClick(); Application.DoEvents(); }
        var queue = Control<ListView>("TransferQueue"); var files = Control<ListView>("RemoteEntries"); var status = Control<Label>("FileStatus");
        PumpPickerUiUntil(() => status.Text == "就绪");
        Click("FileAction批量上传");
        Check(queue.Items.Count == 0 && !Control<Panel>("TransferQueuePanel").Visible && backend.OpenWrites == 0, "cancelled multi-file selection creates no queue tasks or remote I/O");
        picker.Files = [first, second]; Click("FileAction批量上传");
        Check(queue.Items.Count == 2 && queue.Items.Cast<ListViewItem>().All(i => i.SubItems[1].Text == "待开始") && backend.OpenWrites == 0,
            "batch upload stages explicit pending jobs without starting a transfer");
        queue.Items[1].Selected = true; queue.Select(); Click("CancelSelected");
        Check(queue.Items[1].SubItems[1].Text == "取消" && queue.Items[0].SubItems[1].Text == "待开始", "selected waiting-job cancellation leaves the other queued file untouched");
        CompletePickerUi(view.RunQueue());
        Check(backend.Files.ContainsKey("/one.bin") && !backend.Files.ContainsKey("/two.bin") && queue.Items[0].SubItems[1].Text == "成功",
            "queue executes the remaining upload and refreshes the listing after protocol confirmation");
        Click("ClearQueue");
        Check(queue.Items.Count == 0 && !Control<Panel>("TransferQueuePanel").Visible, "finished queue entries can be cleared without changing remote files");
        picker.Files = [Path.Combine(sources, "missing.bin"), second]; Click("FileAction批量上传");
        int writes = backend.OpenWrites; CompletePickerUi(view.RunQueue());
        Check(queue.Items[0].SubItems[1].Text == "失败" && queue.Items[1].SubItems[1].Text == "待开始" && backend.OpenWrites == writes && Control<Button>("RunQueue").Enabled,
            "failed queue job pauses remaining uploads and offers explicit continuation without replaying the failed task");
        view.ConfirmQueueAction = (_, _) => false; Click("RunQueue");
        Check(queue.Items[1].SubItems[1].Text == "待开始" && backend.OpenWrites == writes, "declining post-failure verification cannot resume waiting transfers");
        view.ConfirmQueueAction = (_, _) => true; Click("RunQueue"); PumpPickerUiUntil(() => queue.Items[1].SubItems[1].Text == "成功" && Control<Button>("ClearQueue").Enabled);
        Check(queue.Items[0].SubItems[1].Text == "失败" && queue.Items[1].SubItems[1].Text == "成功" && backend.Files["/two.bin"].SequenceEqual(new byte[] { 3, 4, 5 }),
            "explicit continuation starts only the pending job and retains the failed task result");
        queue.Items[0].Selected = true; queue.Select(); Application.DoEvents();
        Check(Control<Button>("RetryQueue").Enabled, "failed tasks expose a separate verified retry action rather than automatic retry");
        view.ConfirmQueueAction = (_, _) => false; Click("RetryQueue"); Check(queue.Items[0].SubItems[1].Text == "失败", "declining verified retry leaves the failed job unchanged");
        view.ConfirmQueueAction = (_, _) => true; Click("RetryQueue");
        Check(queue.Items[0].SubItems[1].Text == "待开始", "confirmed verified retry stages the failed item without immediately starting I/O");
        File.WriteAllBytes(Path.Combine(sources, "missing.bin"), [8]); CompletePickerUi(view.RunQueue());
        Check(queue.Items.Cast<ListViewItem>().All(i => i.SubItems[1].Text == "成功") && backend.Files.ContainsKey("/missing.bin"), "verified retry can explicitly transmit a repaired failed source");
        Click("ClearQueue");
        picker.Files = [first]; Click("FileAction批量上传"); view.ConfirmQueueAction = (_, _) => false;
        writes = backend.OpenWrites; CompletePickerUi(view.RunQueue());
        Check(queue.Items[0].SubItems[1].Text == "取消" && backend.OpenWrites == writes && backend.Files["/one.bin"].SequenceEqual(new byte[] { 1, 2 }), "declining upload overwrite cancels that job without modifying its existing remote target");
        Click("ClearQueue");
        foreach (ListViewItem item in files.Items) item.Selected = true; files.Select(); Application.DoEvents();
        Check(Control<Button>("FileAction批量下载").Enabled && !Control<Button>("FileAction下载").Enabled && !Control<Button>("FileAction删除").Enabled,
            "multiple remote files enable batch download while single-item operations stay disabled");
        Click("FileAction批量下载"); Check(queue.Items.Count == 0, "cancelled destination-folder selection adds no download jobs");
        picker.Folder = Path.Combine(root, "batch-downloads"); Directory.CreateDirectory(picker.Folder);
        Click("FileAction批量下载");
        CompletePickerUi(view.RunQueue());
        Check(queue.Items.Count == 3 && queue.Items.Cast<ListViewItem>().All(i => i.SubItems[1].Text == "成功")
            && File.ReadAllBytes(Path.Combine(picker.Folder, "one.bin")).SequenceEqual(new byte[] { 1, 2 })
            && File.ReadAllBytes(Path.Combine(picker.Folder, "two.bin")).SequenceEqual(new byte[] { 3, 4, 5 }),
            "batch download transfers selected files serially into the chosen folder");
        Click("ClearQueue"); foreach (ListViewItem row in files.Items) row.Selected = true; files.Select(); Click("FileAction批量下载");
        int reads = backend.OpenReads; CompletePickerUi(view.RunQueue());
        Check(queue.Items.Cast<ListViewItem>().All(i => i.SubItems[1].Text == "取消") && backend.OpenReads == reads, "declining local overwrite preserves existing downloaded files without starting remote reads");
        Click("ClearQueue");
        backend.Listing = _ => [new("bad:name.txt", "/bad:name.txt", false, 1, default), new("bad?name.txt", "/bad?name.txt", false, 1, default)];
        Click("FileAction刷新"); PumpPickerUiUntil(() => status.Text == "就绪");
        foreach (ListViewItem row in files.Items) row.Selected = true; files.Select(); Click("FileAction批量下载");
        Check(queue.Items.Count == 0 && status.Text.Contains("冲突"), "sanitized remote names that collide on Windows are rejected before a batch is queued");
        picker.Files = [first, second]; Click("FileAction批量上传");
        UiTheme.Apply(host, InterfaceTheme.Dark);
        using (var bitmap = new System.Drawing.Bitmap(view.Width, view.Height)) { view.DrawToBitmap(bitmap, view.ClientRectangle); bitmap.Save(Path.Combine(output, "queue-dark.png")); }
        CompletePickerUi(view.CloseAsync()); host.Close(); Application.DoEvents();
        Check(backend.DisposeCalls == 1, "queue view close retains exactly-once transfer connection disposal");
        QueueCloseUi(root, output);
    }
    static void QueueCloseUi(string root, string output)
    {
        var backend = new MemoryRemoteFileSystem { BlockReadCompletion = true };
        backend.Files["/one.bin"] = new byte[200000]; backend.Files["/two.bin"] = [9];
        string folder = Path.Combine(root, "queue-close"); Directory.CreateDirectory(folder);
        var picker = new QueuePicker { Folder = folder };
        using var host = new Form { Width = 1200, Height = 800, Font = UiTheme.BodyFont };
        var view = new FileView(new FileConnection(Protocol.FTP, backend), "/", picker) { Dock = DockStyle.Fill };
        host.Controls.Add(view); host.Show();
        var files = view.Controls.Find("RemoteEntries", true).OfType<ListView>().Single();
        var status = view.Controls.Find("FileStatus", true).OfType<Label>().Single();
        PumpPickerUiUntil(() => status.Text == "就绪");
        foreach (ListViewItem row in files.Items) row.Selected = true; files.Select();
        view.Controls.Find("FileAction批量下载", true).OfType<Button>().Single().PerformClick();
        var run = view.RunQueue(); PumpPickerUiUntil(() => backend.ReadCompletionBlocked.Task.IsCompleted);
        var queue = view.Controls.Find("TransferQueue", true).OfType<ListView>().Single();
        Check(queue.Items[0].SubItems[1].Text == "传输中" && queue.Items[1].SubItems[1].Text == "待开始",
            "active queue exposes separate running and waiting task states");
        using (var bitmap = new System.Drawing.Bitmap(view.Width, view.Height)) { view.DrawToBitmap(bitmap, view.ClientRectangle); bitmap.Save(Path.Combine(output, "queue-running.png")); }
        view.Controls.Find("CancelPending", true).OfType<Button>().Single().PerformClick();
        Check(queue.Items[1].SubItems[1].Text == "取消" && !run.IsCompleted && !backend.ReadCompletionCancelled,
            "cancel-all-waiting leaves the active transfer and its protocol confirmation intact");
        view.Controls.Find("CancelCurrent", true).OfType<Button>().Single().PerformClick(); CompletePickerUi(run);
        Check(queue.Items.Cast<ListViewItem>().All(i => i.SubItems[1].Text == "取消") && backend.OpenReads == 1
            && !File.Exists(Path.Combine(folder, "one.bin")) && !Directory.GetFiles(folder, "*.part").Any(),
            "cancelling current after cancelling waiting tasks prevents submission and removes local staging");
        CompletePickerUi(view.CloseAsync()); host.Close(); Application.DoEvents();
        // Close with both active and waiting work: lifetime cancellation must prevent any later file start.
        backend = new MemoryRemoteFileSystem { BlockReadCompletion = true }; backend.Files["/one.bin"] = [1]; backend.Files["/two.bin"] = [2];
        using var secondHost = new Form { Width = 1000, Height = 750 };
        view = new FileView(new FileConnection(Protocol.SFTP, backend), "/", picker) { Dock = DockStyle.Fill }; secondHost.Controls.Add(view); secondHost.Show();
        status = view.Controls.Find("FileStatus", true).OfType<Label>().Single(); files = view.Controls.Find("RemoteEntries", true).OfType<ListView>().Single();
        PumpPickerUiUntil(() => status.Text == "就绪");
        foreach (ListViewItem row in files.Items) row.Selected = true; files.Select();
        view.Controls.Find("FileAction批量下载", true).OfType<Button>().Single().PerformClick();
        run = view.RunQueue(); PumpPickerUiUntil(() => backend.ReadCompletionBlocked.Task.IsCompleted);
        CompletePickerUi(view.CloseAsync()); CompletePickerUi(run);
        Check(backend.OpenReads == 1 && backend.ReadCompletionCancelled && backend.DisposeCalls == 1,
            "closing a queue cancels active I/O and waiting tasks and awaits cleanup without starting a second file");
        secondHost.Close(); Application.DoEvents();
    }
}
