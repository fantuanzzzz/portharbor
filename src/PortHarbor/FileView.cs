using System.Diagnostics;

namespace PortHarbor;

public sealed partial class FileView : UserControl
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    readonly IFileTransfer connection;
    readonly IFilePicker filePicker;
    readonly TextBox path = new() { Name = "RemotePath", Width = 340, PlaceholderText = "远程路径，例如 /home/user" };
    readonly ListView list = new() { Name = "RemoteEntries", Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
    readonly Label status = new() { Name = "FileStatus", Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly Label emptyState = new() { Name = "FileEmptyState", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "目录为空", Visible = false, Padding = new Padding(16) };
    readonly Panel emptyHost = new() { Dock = DockStyle.Fill, Visible = false };
    readonly FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(UiTheme.Gap), WrapContents = true };
    readonly Button cancel = new UiButton { Name = "CancelFileOperation", Text = "取消", AutoSize = true, Enabled = false };
    CancellationTokenSource? taskStop;
    TaskCompletionSource? operationFinished;
    bool busy, connected, closing;
    bool listingValid;
    string loadedPath = "/", completionText = "就绪";
    ConnectionDiagnostic? lastError;
    int operationVersion;
    readonly Dictionary<string, Button> actions = new(StringComparer.Ordinal);
    IReadOnlyList<RemoteEntry> entries = [];
    int sortColumn;
    bool sortAscending = true;
    internal event Action<ConnectionDiagnostic>? Diagnostic;
    public FileView(Session s, IHostKeyTrust hostKeyTrust, string password, Func<string, bool> confirm, IFilePicker? filePicker = null, SshAuthenticationPromptHandler? prompt = null)
        : this(FileTransferFactory.Create(s, hostKeyTrust, password, confirm, prompt), s.RemotePath, filePicker) { }

    // The view owns and disposes the supplied transfer instance.
    public FileView(IFileTransfer connection, string initialPath, IFilePicker? filePicker = null, IMultiFilePicker? multiFilePicker = null, IFolderPicker? folderPicker = null)
    {
        this.filePicker = filePicker ?? new WindowsFilePicker(this);
        this.multiFilePicker = multiFilePicker ?? this.filePicker as IMultiFilePicker ?? new WindowsFilePicker(this);
        this.folderPicker = folderPicker ?? this.filePicker as IFolderPicker ?? new WindowsFilePicker(this);
        this.connection = connection; connection.Diagnostic += OnDiagnostic; path.Text = initialPath;
        list.Columns.Add("名称 ↑", 300); list.Columns.Add("类型", 80); list.Columns.Add("字节", 110); list.Columns.Add("修改时间", 220);
        list.ColumnClick += (_, e) => { if (!listingValid || busy) return; sortAscending = sortColumn == e.Column ? !sortAscending : true; sortColumn = e.Column; RenderEntries(); };
        list.SelectedIndexChanged += (_, _) => UpdateActionState();
        toolbar.Controls.Add(path);
        Add("前往", async () => await LoadPath());
        Add("上级", async () => { path.Text = ParentPath(loadedPath); await LoadPath(); });
        Add("刷新", async () => await LoadPath());
        Add("上传", Upload); Add("下载", Download); Add("新建目录", Mkdir); Add("重命名", Rename); Add("删除", Delete);
        Add("批量上传", QueueUploads); Add("批量下载", QueueDownloads);
        cancel.Click += (_, _) => { cancel.Enabled = false; ShowStatus("正在取消；等待当前操作及清理完成…", UiRole.Warning); taskStop?.Cancel(); }; toolbar.Controls.Add(cancel);
        path.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LoadPath(); } };
        path.TextChanged += (_, _) => UpdateActionState();
        list.DoubleClick += async (_, _) => { if (Selected is { Directory: true } f) { path.Text = f.Path; await LoadPath(); } else await Download(); };
        emptyHost.Controls.Add(emptyState);
        Controls.Add(list); Controls.Add(emptyHost); Controls.Add(status); Controls.Add(toolbar);
        InitializeTransferQueue();
        UiTheme.Role(status, UiRole.Muted);
        ParentChanged += (_, _) => UiTheme.Refresh(this);
        HandleCreated += async (_, _) => { if (!connected && !busy) await LoadPath(); };
    }
    void Add(string title, Func<Task> action)
    {
        var b = new UiButton { Name = "FileAction" + title, Text = title, AutoSize = true, Kind = title == "删除" ? UiButtonKind.Danger : title == "前往" ? UiButtonKind.Primary : UiButtonKind.Secondary };
        b.Click += async (_, _) => await action(); toolbar.Controls.Add(b);
        actions[title] = b;
    }
    RemoteEntry? Selected => list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as RemoteEntry;
    public static string Join(string directory, string name) => directory.TrimEnd('/') + "/" + name;
    static string ParentPath(string value) { var p = value.TrimEnd('/'); int at = p.LastIndexOf('/'); return at <= 0 ? "/" : p[..at]; }
    static bool ValidName(string name) => name.Length > 0 && name is not ("." or "..") && !name.Any(c => c is '/' or '\\' || char.IsControl(c));
    void ShowStatus(string text, UiRole role)
    {
        status.Text = text; UiTheme.Role(status, role);
    }
    void UpdateActionState()
    {
        bool active = !busy && !closing;
        if (actions.GetValueOrDefault("前往") is { } go) go.Enabled = active;
        if (actions.GetValueOrDefault("刷新") is { } refresh) refresh.Enabled = active;
        if (actions.GetValueOrDefault("上级") is { } parent) parent.Enabled = active && listingValid && loadedPath.TrimEnd('/').Length > 0;
        if (actions.GetValueOrDefault("上传") is { } upload) upload.Enabled = active && connected && listingValid;
        if (actions.GetValueOrDefault("新建目录") is { } mkdir) mkdir.Enabled = active && connected && listingValid;
        bool selected = active && listingValid && list.SelectedItems.Count == 1 && Selected != null;
        if (actions.GetValueOrDefault("下载") is { } download) download.Enabled = selected && Selected is { Directory: false };
        if (actions.GetValueOrDefault("重命名") is { } rename) rename.Enabled = selected;
        if (actions.GetValueOrDefault("删除") is { } delete) delete.Enabled = selected;
        if (actions.GetValueOrDefault("批量上传") is { } batchUpload) batchUpload.Enabled = active && listingValid;
        if (actions.GetValueOrDefault("批量下载") is { } batchDownload) batchDownload.Enabled = active && listingValid && list.SelectedItems.Count > 0
            && list.SelectedItems.Cast<ListViewItem>().All(i => i.Tag is RemoteEntry { Directory: false });
        UpdateQueueActions();
    }
    static string FormatBytes(long value) => value < 1024 ? $"{value:N0} B" : value < 1024 * 1024 ? $"{value / 1024d:N1} KB" : $"{value / 1024d / 1024d:N1} MB";
    void ShowProgress(string operation, TransferProgress progress, Stopwatch timer, int version)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(() => ShowProgress(operation, progress, timer, version)); return; }
        if (!busy || closing || version != operationVersion || taskStop?.IsCancellationRequested == true) return;
        double seconds = Math.Max(.001, timer.Elapsed.TotalSeconds);
        string total = progress.TotalBytes is >= 0 ? FormatBytes(progress.TotalBytes.Value) : "未知大小";
        ShowStatus($"{operation} {FormatBytes(progress.BytesTransferred)} / {total} · {FormatBytes((long)(progress.BytesTransferred / seconds))}/秒 · 等待完成确认", UiRole.Normal);
    }
    void OnDiagnostic(ConnectionDiagnostic diagnostic)
    {
        Diagnostic?.Invoke(diagnostic);
        if (diagnostic.ErrorCode != ConnectionErrorCode.None) lastError = diagnostic;
    }
    async Task Work(Func<CancellationToken, Task> operation, string operationName = "操作")
    {
        if (busy || closing) return;
        // Dialog/fixture lifetime can remove WinForms' ambient context; all view continuations remain UI-owned.
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext) SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        operationVersion++; lastError = null; completionText = operationName == "加载目录" ? "就绪" : operationName + "已完成";
        busy = true; taskStop = new(); toolbar.Enabled = false; cancel.Enabled = true; list.Enabled = false; UpdateActionState();
        operationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Keep cancellation available while the rest of the toolbar is disabled.
        toolbar.Enabled = true; foreach (Control c in toolbar.Controls) if (c != cancel) c.Enabled = false;
        ShowStatus("处理中…", UiRole.Muted);
        try { await operation(taskStop.Token); if (!closing) ShowStatus(taskStop.IsCancellationRequested ? "任务已取消；请刷新目录并核对操作结果。" : completionText, taskStop.IsCancellationRequested ? UiRole.Warning : UiRole.Success); }
        catch (OperationCanceledException) { if (!closing) ShowStatus("任务已取消；请刷新目录并核对操作结果。", UiRole.Warning); }
        catch (Exception) { if (!closing) { ShowStatus(operationName + "失败；" + (lastError == null ? "" : DiagnosticPresenter.Error(lastError) + "；") + "请刷新目录并核对操作结果。", UiRole.Error); if (operationName == "加载目录" && !listingValid) { emptyState.Text = "目录未能加载；可点击“刷新”重试。"; emptyHost.Visible = emptyState.Visible = true; } } }
        finally
        {
            taskStop.Dispose(); taskStop = null; busy = false;
            if (closing) await connection.DisposeAsync();
            else { foreach (Control c in toolbar.Controls) c.Enabled = true; cancel.Enabled = false; list.Enabled = true; UpdateActionState(); }
            operationFinished?.TrySetResult(); operationFinished = null;
        }
    }

    internal async Task CloseAsync()
    {
        closing = true;
        transferQueue.CancelAll();
        taskStop?.Cancel();
        connection.Diagnostic -= OnDiagnostic;
        if (busy && operationFinished is { } currentOperation)
            await currentOperation.Task;
        else
            await connection.DisposeAsync();
    }
    async Task RefreshListing(CancellationToken token)
    {
        string requested = string.IsNullOrWhiteSpace(path.Text) ? "/" : path.Text.Trim();
        listingValid = false; list.Enabled = false; emptyHost.Visible = false;
        try
        {
            var listed = await connection.ListAsync(requested, token, OperationTimeout);
            if (closing) return;
            entries = listed.ToArray(); loadedPath = requested; path.Text = loadedPath; listingValid = true; RenderEntries();
        }
        catch
        {
            entries = []; list.Items.Clear(); emptyState.Text = "目录未能加载；可点击“刷新”重试。"; emptyHost.Visible = emptyState.Visible = true; throw;
        }
        finally { if (!closing) list.Enabled = true; }
    }
    void RenderEntries()
    {
        IEnumerable<RemoteEntry> ordered = sortColumn switch
        {
            1 => entries.OrderBy(f => f.Directory ? "目录" : "文件", StringComparer.Ordinal),
            2 => entries.OrderBy(f => f.Directory ? -1 : f.Size),
            3 => entries.OrderBy(f => f.Modified),
            _ => entries.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        if (!sortAscending) ordered = ordered.Reverse();
        var sorted = ordered.ToList();
        ordered = sorted.Where(f => f.Directory).Concat(sorted.Where(f => !f.Directory));
        string[] headers = ["名称", "类型", "字节", "修改时间"];
        for (int i = 0; i < headers.Length; i++) list.Columns[i].Text = headers[i] + (i == sortColumn ? sortAscending ? " ↑" : " ↓" : "");
        var selectedPaths = list.SelectedItems.Cast<ListViewItem>().Select(i => ((RemoteEntry)i.Tag!).Path).ToHashSet(StringComparer.Ordinal);
        list.BeginUpdate(); list.Items.Clear();
        foreach (var f in ordered)
        { var item = new ListViewItem([f.Name, f.Directory ? "目录" : "文件", f.Directory ? "" : f.Size < 0 ? "未知" : f.Size.ToString("N0"), f.Modified == default ? "未知" : f.Modified.ToString("yyyy-MM-dd HH:mm:ss")]) { Tag = f }; list.Items.Add(item); if (selectedPaths.Contains(f.Path)) item.Selected = true; }
        list.EndUpdate(); emptyState.Text = entries.Count == 0 ? "目录为空" : ""; emptyHost.Visible = emptyState.Visible = entries.Count == 0; UpdateActionState();
    }
    Task LoadPath() => Work(async token => { if (!connected) { ShowStatus("正在连接…", UiRole.Muted); await connection.ConnectAsync(token, ConnectTimeout); connected = true; } ShowStatus("正在加载目录…", UiRole.Muted); await RefreshListing(token); }, "加载目录");
    internal async Task Upload()
    {
        if (busy || closing || !listingValid) return;
        string? file = filePicker.OpenFile(new()); if (file == null) return;
        string remote = Join(loadedPath, Path.GetFileName(file));
        await Work(async token =>
        {
            bool exists = await connection.ExistsAsync(remote, token, OperationTimeout);
            if (exists && MessageBox.Show(this, "远程文件已存在，覆盖？\n" + remote, "上传", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { completionText = "已放弃覆盖，未上传"; return; }
            int version = operationVersion; var timer = Stopwatch.StartNew(); var progress = new Progress<TransferProgress>(p => ShowProgress("上传", p, timer, version));
            await connection.UploadAsync(file, remote, token, OperationTimeout, progress);
            path.Text = loadedPath; await RefreshListing(token);
        }, "上传");
    }
    internal async Task Download()
    {
        if (busy || closing || !listingValid || list.SelectedItems.Count != 1 || Selected is not { Directory: false } f) return;
        // Remote names cannot escape the user-selected local destination.
        string name = string.Concat(f.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string? file = filePicker.SaveFile(new(FileName: name)); if (file == null) return;
        await Work(async token =>
        {
            int version = operationVersion; var timer = Stopwatch.StartNew(); var progress = new Progress<TransferProgress>(p => ShowProgress("下载", p, timer, version));
            await connection.DownloadAsync(f.Path, file, token, OperationTimeout, progress);
        }, "下载");
    }
    async Task Mkdir()
    {
        if (busy || closing || !listingValid) return; string? name = TextPrompt.Ask(this, "新建目录", "目录名"); if (name == null) return;
        if (!ValidName(name)) { MessageBox.Show(this, "名称无效。"); return; }
        await Work(async token => { await connection.MkdirAsync(Join(loadedPath, name), token, OperationTimeout); path.Text = loadedPath; await RefreshListing(token); }, "新建目录");
    }
    async Task Rename()
    {
        if (busy || closing || !listingValid || Selected is not { } f) return; string? name = TextPrompt.Ask(this, "重命名", "新名称", f.Name); if (name == null) return;
        if (!ValidName(name)) { MessageBox.Show(this, "名称无效。"); return; }
        await Work(async token => { await connection.RenameAsync(f.Path, Join(loadedPath, name), token, OperationTimeout); path.Text = loadedPath; await RefreshListing(token); }, "重命名");
    }
    async Task Delete()
    {
        if (busy || closing || !listingValid || Selected is not { } f || MessageBox.Show(this, "永久删除此项目？（目录必须为空）\n" + f.Path, "删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await Work(async token => { await connection.DeleteAsync(f, token, OperationTimeout); path.Text = loadedPath; await RefreshListing(token); }, "删除");
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing = true;
            transferQueue.CancelAll();
            taskStop?.Cancel();
            connection.Diagnostic -= OnDiagnostic;
            if (!busy) BackgroundTask.Observe(connection.DisposeAsync().AsTask());
        }
        base.Dispose(disposing);
    }
}
