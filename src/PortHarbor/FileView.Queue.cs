using System.Diagnostics;

namespace PortHarbor;

public sealed partial class FileView
{
    readonly TransferQueue transferQueue = new();
    readonly IMultiFilePicker multiFilePicker;
    readonly IFolderPicker folderPicker;
    readonly ListView queueList = new() { Name = "TransferQueue", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    readonly Panel queuePanel = new() { Name = "TransferQueuePanel", Dock = DockStyle.Bottom, Height = 220, Visible = false };
    readonly Dictionary<string, Button> queueButtons = [];
    internal Func<string, string, bool>? ConfirmQueueAction { get; set; }
    bool ConfirmQueue(string message, string title) => ConfirmQueueAction?.Invoke(message, title)
        ?? MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    TransferJob? SelectedJob => queueList.SelectedItems.Count == 1 ? queueList.SelectedItems[0].Tag as TransferJob : null;
    void InitializeTransferQueue()
    {
        queueList.Columns.Add("方向 / 文件", 240); queueList.Columns.Add("状态", 90); queueList.Columns.Add("目标", 240); queueList.Columns.Add("结果 / 进度", 400);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        foreach (var (name, title, action) in new (string, string, Action)[]
        {
            ("RunQueue", "开始 / 继续队列", StartQueue),
            ("CancelCurrent", "取消当前", () => transferQueue.CancelCurrent()),
            ("CancelPending", "取消全部等待", () => transferQueue.CancelPending()),
            ("CancelSelected", "取消所选等待", () => { if (SelectedJob is { } job) transferQueue.CancelPending(job.Id); }),
            ("RetryQueue", "核对后重试所选…", RetrySelectedJob),
            ("ClearQueue", "清理已结束", () => transferQueue.ClearFinished())
        })
        {
            var button = new UiButton { Name = name, Text = title, AutoSize = true };
            button.Click += (_, _) => action(); buttons.Controls.Add(button); queueButtons[name] = button;
        }
        queuePanel.Controls.Add(queueList); queuePanel.Controls.Add(buttons); Controls.Add(queuePanel);
        // Bottom task list sits above the final operation status, with the file list filling the remaining space.
        Controls.SetChildIndex(queuePanel, Controls.GetChildIndex(status));
        queueList.SelectedIndexChanged += (_, _) => UpdateQueueActions();
        transferQueue.Changed += RenderQueue; UpdateQueueActions();
    }
    void UpdateQueueActions()
    {
        if (queueButtons.Count == 0) return;
        bool idle = !busy && !closing && !transferQueue.IsRunning;
        queueButtons["RunQueue"].Enabled = idle && connected && transferQueue.Jobs.Any(j => j.State == TransferJobState.Pending);
        queueButtons["CancelCurrent"].Enabled = !closing && transferQueue.IsRunning && transferQueue.Jobs.Any(j => j.State == TransferJobState.Running);
        queueButtons["CancelPending"].Enabled = !closing && transferQueue.Jobs.Any(j => j.State == TransferJobState.Pending);
        queueButtons["CancelSelected"].Enabled = !closing && SelectedJob?.State == TransferJobState.Pending;
        queueButtons["RetryQueue"].Enabled = idle && SelectedJob?.State is TransferJobState.Failed or TransferJobState.Cancelled;
        queueButtons["ClearQueue"].Enabled = idle && transferQueue.Jobs.Any(j => j.State is TransferJobState.Succeeded or TransferJobState.Failed or TransferJobState.Cancelled);
    }
    void RenderQueue()
    {
        if (IsDisposed || closing) return;
        if (InvokeRequired) { BeginInvoke(RenderQueue); return; }
        Guid? selected = SelectedJob?.Id; queueList.BeginUpdate(); queueList.Items.Clear();
        foreach (var job in transferQueue.Jobs)
        {
            string state = job.State switch { TransferJobState.Pending => "待开始", TransferJobState.Running => "传输中", TransferJobState.Succeeded => "成功", TransferJobState.Failed => "失败", _ => "取消" };
            var row = new ListViewItem([(job.Direction == TransferDirection.Upload ? "上传 · " : "下载 · ") + (job.Direction == TransferDirection.Upload ? Path.GetFileName(job.Source) : job.Source.Split('/').Last()), state, job.Destination, job.Result]) { Tag = job };
            queueList.Items.Add(row); if (job.Id == selected) row.Selected = true;
        }
        queueList.EndUpdate(); queuePanel.Visible = transferQueue.Jobs.Count > 0; UpdateQueueActions();
    }
    async Task QueueUploads()
    {
        if (busy || closing || !listingValid) return;
        try
        {
            var files = multiFilePicker.OpenFiles(new());
            if (files.Count == 0) return;
            transferQueue.Add(files.Select(file => (TransferDirection.Upload, file, Join(loadedPath, Path.GetFileName(file)))));
            ShowStatus($"已加入 {files.Count} 项；点击开始队列，失败时暂停等待任务", UiRole.Normal);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { ShowStatus("未能加入队列，请检查所选文件及队列容量", UiRole.Error); }
        await Task.CompletedTask;
    }
    async Task QueueDownloads()
    {
        if (busy || closing || !listingValid) return;
        var files = list.SelectedItems.Cast<ListViewItem>().Select(i => (RemoteEntry)i.Tag!).ToArray();
        if (files.Length == 0 || files.Any(f => f.Directory)) return;
        try
        {
            string? folder = folderPicker.SelectFolder(); if (folder == null) return;
            string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var names = files.Select(f => SafeName(f.Name)).ToArray();
            string[] reserved = ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).Select(i => "COM" + i), .. Enumerable.Range(1, 9).Select(i => "LPT" + i)];
            if (!Path.IsPathFullyQualified(folder) || names.Any(n => !ValidName(n) || n.EndsWith('.') || n.EndsWith(' ') || reserved.Contains(n.Split('.')[0], StringComparer.OrdinalIgnoreCase)) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            { ShowStatus("本地文件名无效或清理后冲突；请分别使用单文件下载另选名称", UiRole.Error); return; }
            transferQueue.Add(files.Select((f, i) => (TransferDirection.Download, f.Path, Path.Combine(folder, names[i]))));
            ShowStatus($"已加入 {files.Length} 项；本地覆盖会逐项确认", UiRole.Normal);
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { ShowStatus("未能加入下载队列，请检查目录及队列容量", UiRole.Error); }
        await Task.CompletedTask;
    }
    void RetrySelectedJob()
    {
        if (SelectedJob is not { } job || busy || closing) return;
        if (!ConfirmQueue("已通过刷新或新连接核对目标、.part 和 .bak，并确认可以重新传输？\n未知提交结果不能直接覆盖。", "核对后重试")) return;
        transferQueue.Retry(job.Id, true);
    }
    void StartQueue()
    {
        if (busy || closing) return;
        if (transferQueue.Jobs.Any(j => j.State == TransferJobState.Failed) && !ConfirmQueue("已核对失败任务的目标及暂存/备份，可以继续剩余等待任务？失败任务本身不会自动重试。", "核对后继续")) return;
        BackgroundTask.Observe(RunQueue(), _ => ShowStatus("队列未完成，请核对结果", UiRole.Error));
    }
    internal Task RunQueue() => Work(async token =>
    {
        await transferQueue.RunAsync(async (job, itemToken) =>
        {
            lastError = null; int version = operationVersion; var timer = Stopwatch.StartNew();
            bool exists = job.Direction == TransferDirection.Upload ? await connection.ExistsAsync(job.Destination, itemToken, OperationTimeout) : File.Exists(job.Destination);
            if (exists && !ConfirmQueue("目标文件已存在，覆盖？\n" + job.Destination, "队列覆盖确认"))
            { transferQueue.CancelCurrent(); throw new OperationCanceledException(itemToken); }
            var progress = new Progress<TransferProgress>(p =>
            {
                if (IsDisposed || closing || !IsHandleCreated) return;
                if (InvokeRequired) { BeginInvoke(() => UpdateQueueProgress(job, p, timer, version)); return; }
                UpdateQueueProgress(job, p, timer, version);
            });
            if (job.Direction == TransferDirection.Upload) await connection.UploadAsync(job.Source, job.Destination, itemToken, OperationTimeout, progress);
            else await connection.DownloadAsync(job.Source, job.Destination, itemToken, OperationTimeout, progress);
            return "传输与目标确认完成";
        }, token);
        if (!closing && !token.IsCancellationRequested && transferQueue.Jobs.Any(j => j.Direction == TransferDirection.Upload && j.State == TransferJobState.Succeeded))
        { path.Text = loadedPath; await RefreshListing(token); }
        completionText = transferQueue.Jobs.Any(j => j.State == TransferJobState.Failed) ? "队列已暂停；失败结果需核对，等待任务可显式继续" : "队列本轮处理结束，请查看逐项结果";
    }, "队列");
    void UpdateQueueProgress(TransferJob job, TransferProgress p, Stopwatch timer, int version)
    {
        if (IsDisposed || closing || !busy || version != operationVersion || taskStop?.IsCancellationRequested == true) return;
        if (transferQueue.Jobs.FirstOrDefault(j => j.Id == job.Id)?.State != TransferJobState.Running) return;
        ShowProgress(job.Direction == TransferDirection.Upload ? "上传" : "下载", p, timer, version);
        var row = queueList.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is TransferJob j && j.Id == job.Id);
        if (row != null) row.SubItems[3].Text = $"{FormatBytes(p.BytesTransferred)} / {(p.TotalBytes is >= 0 ? FormatBytes(p.TotalBytes.Value) : "未知大小")} · 等待完成确认";
    }
}
