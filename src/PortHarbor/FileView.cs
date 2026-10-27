namespace PortHarbor;

public sealed class FileView : UserControl
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    readonly IFileTransfer connection;
    readonly IFilePicker filePicker;
    readonly TextBox path = new() { Width = 340 };
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    readonly FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, Height = 72, Padding = new Padding(6), WrapContents = true };
    readonly Button cancel = new() { Text = "取消", AutoSize = true, Enabled = false };
    CancellationTokenSource? taskStop;
    TaskCompletionSource? operationFinished;
    bool busy, connected, closing;
    public FileView(Session s, IHostKeyTrust hostKeyTrust, string password, Func<string, bool> confirm, IFilePicker? filePicker = null)
        : this(FileTransferFactory.Create(s, hostKeyTrust, password, confirm), s.RemotePath, filePicker) { }

    // The view owns and disposes the supplied transfer instance.
    public FileView(IFileTransfer connection, string initialPath, IFilePicker? filePicker = null)
    {
        this.filePicker = filePicker ?? new WindowsFilePicker(this);
        this.connection = connection; connection.Diagnostic += OnDiagnostic; path.Text = initialPath;
        list.Columns.Add("名称", 300); list.Columns.Add("类型", 80); list.Columns.Add("字节", 110); list.Columns.Add("修改时间", 170);
        toolbar.Controls.Add(path);
        Add("前往", async () => await LoadPath());
        Add("上级", async () => { path.Text = ParentPath(path.Text); await LoadPath(); });
        Add("刷新", async () => await LoadPath());
        Add("上传", Upload); Add("下载", Download); Add("新建目录", Mkdir); Add("重命名", Rename); Add("删除", Delete);
        cancel.Click += (_, _) => taskStop?.Cancel(); toolbar.Controls.Add(cancel);
        path.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await LoadPath(); } };
        list.DoubleClick += async (_, _) => { if (Selected is { Directory: true } f) { path.Text = f.Path; await LoadPath(); } else await Download(); };
        Controls.Add(list); Controls.Add(status); Controls.Add(toolbar);
        HandleCreated += async (_, _) => { if (!connected && !busy) await Work(async token => { await connection.ConnectAsync(token, ConnectTimeout); connected = true; await RefreshListing(token); }); };
    }
    void Add(string title, Func<Task> action) { var b = new Button { Text = title, AutoSize = true }; b.Click += async (_, _) => await action(); toolbar.Controls.Add(b); }
    RemoteEntry? Selected => list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as RemoteEntry;
    public static string Join(string directory, string name) => directory.TrimEnd('/') + "/" + name;
    static string ParentPath(string value) { var p = value.TrimEnd('/'); int at = p.LastIndexOf('/'); return at <= 0 ? "/" : p[..at]; }
    static bool ValidName(string name) => name.Length > 0 && name is not ("." or "..") && !name.Any(c => c is '/' or '\\' || char.IsControl(c));
    void SetStatus(string text) { if (!IsDisposed && IsHandleCreated && !closing) BeginInvoke(() => status.Text = text); }
    void OnDiagnostic(ConnectionDiagnostic diagnostic) { SetStatus(DiagnosticPresenter.Status(diagnostic)); }
    async Task Work(Func<CancellationToken, Task> operation)
    {
        if (busy || closing) return;
        busy = true; taskStop = new(); toolbar.Enabled = false; cancel.Enabled = true;
        operationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Keep cancellation available while the rest of the toolbar is disabled.
        toolbar.Enabled = true; foreach (Control c in toolbar.Controls) if (c != cancel) c.Enabled = false;
        status.Text = "处理中…";
        try { await operation(taskStop.Token); if (!closing) status.Text = taskStop.IsCancellationRequested ? "任务已取消" : "就绪"; }
        catch (OperationCanceledException) { if (!closing) status.Text = "任务已取消；请刷新目录并核对操作结果。"; }
        catch (Exception) { if (!closing) { status.Text = "操作失败"; MessageBox.Show(this, connection.State == ConnectionState.Failed ? "文件操作失败，请根据状态栏中的错误码排查。" : "文件操作未完成。", "PortHarbor", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        finally
        {
            taskStop.Dispose(); taskStop = null; busy = false;
            if (closing) await connection.DisposeAsync();
            else { foreach (Control c in toolbar.Controls) c.Enabled = true; cancel.Enabled = false; }
            operationFinished?.TrySetResult(); operationFinished = null;
        }
    }

    internal async Task CloseAsync()
    {
        closing = true;
        taskStop?.Cancel();
        connection.Diagnostic -= OnDiagnostic;
        if (busy && operationFinished is { } currentOperation)
            await currentOperation.Task;
        else
            await connection.DisposeAsync();
    }
    async Task RefreshListing(CancellationToken token)
    {
        string requested = string.IsNullOrWhiteSpace(path.Text) ? "/" : path.Text;
        var entries = await connection.ListAsync(requested, token, OperationTimeout);
        if (closing) return;
        list.BeginUpdate(); list.Items.Clear();
        foreach (var f in entries.OrderByDescending(f => f.Directory).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        { var item = new ListViewItem([f.Name, f.Directory ? "目录" : "文件", f.Directory ? "" : f.Size.ToString("N0"), f.Modified.ToString("yyyy-MM-dd HH:mm:ss")]) { Tag = f }; list.Items.Add(item); }
        list.EndUpdate();
    }
    Task LoadPath() => Work(async token => { if (!connected) { await connection.ConnectAsync(token, ConnectTimeout); connected = true; } await RefreshListing(token); });
    internal async Task Upload()
    {
        if (busy) return;
        string? file = filePicker.OpenFile(new()); if (file == null) return;
        string remote = Join(path.Text, Path.GetFileName(file));
        await Work(async token =>
        {
            bool exists = await connection.ExistsAsync(remote, token, OperationTimeout);
            if (exists && MessageBox.Show(this, "远程文件已存在，覆盖？\n" + remote, "上传", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var progress = new Progress<TransferProgress>(p => SetStatus($"上传 {p.BytesTransferred:N0} / {p.TotalBytes?.ToString("N0") ?? "?"} 字节"));
            await connection.UploadAsync(file, remote, token, OperationTimeout, progress); await RefreshListing(token);
        });
    }
    internal async Task Download()
    {
        if (busy || Selected is not { Directory: false } f) return;
        // Remote names cannot escape the user-selected local destination.
        string name = string.Concat(f.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string? file = filePicker.SaveFile(new(FileName: name)); if (file == null) return;
        await Work(async token =>
        {
            var progress = new Progress<TransferProgress>(p => SetStatus($"下载 {p.BytesTransferred:N0} / {p.TotalBytes?.ToString("N0") ?? f.Size.ToString("N0")} 字节"));
            await connection.DownloadAsync(f.Path, file, token, OperationTimeout, progress);
        });
    }
    async Task Mkdir()
    {
        if (busy) return; string? name = TextPrompt.Ask(this, "新建目录", "目录名"); if (name == null) return;
        if (!ValidName(name)) { MessageBox.Show(this, "名称无效。"); return; }
        await Work(async token => { await connection.MkdirAsync(Join(path.Text, name), token, OperationTimeout); await RefreshListing(token); });
    }
    async Task Rename()
    {
        if (busy || Selected is not { } f) return; string? name = TextPrompt.Ask(this, "重命名", "新名称", f.Name); if (name == null) return;
        if (!ValidName(name)) { MessageBox.Show(this, "名称无效。"); return; }
        await Work(async token => { await connection.RenameAsync(f.Path, Join(path.Text, name), token, OperationTimeout); await RefreshListing(token); });
    }
    async Task Delete()
    {
        if (busy || Selected is not { } f || MessageBox.Show(this, "永久删除此项目？（目录必须为空）\n" + f.Path, "删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await Work(async token => { await connection.DeleteAsync(f, token, OperationTimeout); await RefreshListing(token); });
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing = true;
            taskStop?.Cancel();
            connection.Diagnostic -= OnDiagnostic;
            if (!busy) _ = connection.DisposeAsync();
        }
        base.Dispose(disposing);
    }
}
