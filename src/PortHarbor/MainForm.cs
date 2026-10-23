using System.Diagnostics;

namespace PortHarbor;

public sealed class MainForm : Form
{
    readonly Store store;
    readonly TreeView tree = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, FullRowSelect = true, ItemHeight = 32, ShowLines = false, ShowRootLines = false, BackColor = Color.FromArgb(242, 245, 250) };
    readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "搜索会话、主机或协议…", Margin = new Padding(8) };
    readonly ConnectionTabs tabs = new();
    readonly Dictionary<TabPage, IDisposable> connections = [];
    readonly ToolStripStatusLabel status = new() { Text = "就绪", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    public MainForm(Store store)
    {
        this.store = store;
        Text = "PortHarbor · 轻量连接工作台"; Size = new Size(1250, 820); MinimumSize = new Size(850, 560);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 9); AutoScaleMode = AutoScaleMode.Dpi; BackColor = Color.White;
        var toolbar = new MenuStrip { BackColor = Color.White, Padding = new Padding(8, 5, 8, 5) };
        MainMenuStrip = toolbar;
        ToolStripMenuItem Menu(string title) { var menu = new ToolStripMenuItem(title); toolbar.Items.Add(menu); return menu; }
        void Item(ToolStripMenuItem menu, string title, Action action) { var item = new ToolStripMenuItem(title); item.Click += (_, _) => Guard(action); menu.DropDownItems.Add(item); }
        var file = Menu("文件");
        Item(file, "导入会话…", Import); Item(file, "导出会话…", Export);
        file.DropDownItems.Add(new ToolStripSeparator()); Item(file, "退出", Close);
        var sessionsMenu = Menu("会话");
        Item(sessionsMenu, "新建连接…", () => Edit(null)); Item(sessionsMenu, "新建根分类…", () => NewGroup(false)); Item(sessionsMenu, "新建子分类…", NewGroup);
        sessionsMenu.DropDownItems.Add(new ToolStripSeparator());
        Item(sessionsMenu, "连接", ConnectSelected); Item(sessionsMenu, "编辑…", () => { if (Selected is { } session) Edit(session); }); Item(sessionsMenu, "删除会话及其密码…", Delete);
        var window = Menu("窗口"); Item(window, "关闭当前标签…", CloseTab);
        var settings = Menu("设置"); Item(settings, "主密码…", ConfigureMasterPassword);
        Item(settings, "打开配置目录", () => Process.Start(new ProcessStartInfo("explorer.exe", store.DirectoryPath) { UseShellExecute = true }));
        var help = Menu("帮助"); Item(help, "关于 PortHarbor", () => MessageBox.Show(this, "PortHarbor 0.1.0 私有预览版\nSSH · Telnet · RDP · FTP · SFTP · Serial · Shell\n功能限制和第三方许可见发行包 README。", "关于 PortHarbor"));
        tree.ImageList = SessionTreeIcons.Create(); tree.ShowPlusMinus = true; tree.ShowLines = true; tree.ShowRootLines = true; tree.ShowNodeToolTips = true;
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 1, BackColor = Color.FromArgb(220, 226, 235), Panel1MinSize = 190 };
        var sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = tree.BackColor };
        var title = new Label { Dock = DockStyle.Top, Height = 43, Text = "会话库", Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Padding = new Padding(0, 8, 0, 0) };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 48, Text = "双击连接 · 右键管理\nCtrl+N 新建 · Ctrl+W 关闭", ForeColor = Color.SlateGray };
        sidebar.Controls.Add(tree); sidebar.Controls.Add(search); sidebar.Controls.Add(title); sidebar.Controls.Add(hint);
        split.Panel1.Controls.Add(sidebar); split.Panel2.Controls.Add(tabs);
        var bar = new StatusStrip(); bar.Items.Add(status); bar.Items.Add(new ToolStripStatusLabel("私有预览版 · 0.1.0"));
        Controls.Add(split); Controls.Add(toolbar); Controls.Add(bar);
        Shown += (_, _) => { split.SplitterDistance = 250; if ((store.HasMasterPassword || store.Data.Credentials.Count > 0) && !EnsureVault()) status.Text = "未解锁主密码：已保存密码暂不可读，可连接并手动输入密码"; };
        search.TextChanged += (_, _) => RefreshTree();
        tree.NodeMouseDoubleClick += (_, _) => Guard(ConnectSelected);
        tree.NodeMouseClick += (_, e) => tree.SelectedNode = e.Node;
        var context = new ContextMenuStrip();
        context.Items.Add("新建根分类", null, (_, _) => Guard(() => NewGroup(false)));
        context.Items.Add("新建子分类", null, (_, _) => Guard(NewGroup));
        context.Items.Add("新建会话", null, (_, _) => Guard(() => Edit(null)));
        context.Items.Add("连接", null, (_, _) => Guard(ConnectSelected));
        context.Items.Add("编辑", null, (_, _) => { if (Selected is { } s) Guard(() => Edit(s)); });
        context.Items.Add("复制会话", null, (_, _) => { if (Selected is { } s) Guard(() => Edit(s with { Id = Guid.NewGuid(), Name = s.Name + " 副本", SavePassword = false })); });
        context.Items.Add("删除会话及其密码", null, (_, _) => Guard(Delete)); tree.ContextMenuStrip = context;
        tabs.CloseRequested += page => CloseTab(page);
        RefreshTree(); Welcome();
        FormClosing += (_, e) =>
        {
            if (tabs.TabPages.Cast<TabPage>().Any(p => p.Tag is Session) && MessageBox.Show(this, "关闭软件并断开所有内嵌连接？\n外部远程桌面窗口不会随之关闭。", "确认退出", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            { e.Cancel = true; return; }
            foreach (var connection in connections.Values) connection.Dispose();
            store.LockVault();
        };
    }
    void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "PortHarbor", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    Session? Selected => tree.SelectedNode?.Tag as Session;
    void RefreshTree()
    {
        Guid? selected = Selected?.Id;
        string? selectedGroup = tree.SelectedNode?.Tag as string;
        string filter = search.Text.Trim();
        tree.BeginUpdate(); tree.Nodes.Clear();
        var folders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        TreeNode Folder(string path)
        {
            if (folders.TryGetValue(path, out var existing)) return existing;
            int separator = path.LastIndexOf('/');
            var nodes = separator < 0 ? tree.Nodes : Folder(path[..separator]).Nodes;
            var node = nodes.Add(separator < 0 ? path : path[(separator + 1)..]);
            node.Tag = path; node.ImageKey = node.SelectedImageKey = "folder"; node.ToolTipText = path;
            folders[path] = node;
            if (string.Equals(path, selectedGroup, StringComparison.OrdinalIgnoreCase)) tree.SelectedNode = node;
            return node;
        }
        foreach (string group in store.Data.Groups.Concat(store.Data.Sessions.Select(s => string.IsNullOrWhiteSpace(s.Group) ? "默认" : s.Group)).Append("默认").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g))
        {
            if (filter.Length == 0 || group.Contains(filter, StringComparison.OrdinalIgnoreCase)) Folder(group);
        }
        foreach (var session in store.Data.Sessions.OrderBy(s => s.Name))
        {
            string group = string.IsNullOrWhiteSpace(session.Group) ? "默认" : session.Group;
            if (filter.Length > 0 && !$"{session.Name} {session.Host} {session.Protocol} {group}".Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var node = Folder(group).Nodes.Add($"{session.Name}  ·  {session.Protocol}");
            node.Tag = session; node.ImageKey = node.SelectedImageKey = session.Protocol is Protocol.FTP or Protocol.SFTP ? "files" : session.Protocol == Protocol.RDP ? "desktop" : "terminal";
            node.ToolTipText = $"{session.Host}:{session.Port}";
            if (session.Id == selected) tree.SelectedNode = node;
        }
        tree.ExpandAll(); tree.EndUpdate(); status.Text = $"{store.Data.Sessions.Count} 个已保存会话 · {(store.IsPortable ? "便携版 · INI" : "安装版配置")}";
    }
    void NewGroup() => NewGroup(true);
    void NewGroup(bool nested)
    {
        string? parent = nested ? tree.SelectedNode?.Tag as string ?? tree.SelectedNode?.Parent?.Tag as string : null;
        string? name = TextPrompt.Ask(this, "新建分类", parent == null ? "根分类名称（不含 /）" : $"在 {parent} 下新建子分类（不含 /）");
        if (name == null) return;
        if (name.Contains('/') || name.Contains('\\')) throw new ArgumentException("单个分类名称不能包含路径分隔符。");
        string path = parent == null ? name.Trim() : parent + "/" + name.Trim();
        store.AddGroup(path); search.Clear(); RefreshTree();
        TreeNode? Find(TreeNodeCollection nodes) { foreach (TreeNode node in nodes) { if (string.Equals(node.Tag as string, path, StringComparison.OrdinalIgnoreCase)) return node; var child = Find(node.Nodes); if (child != null) return child; } return null; }
        tree.SelectedNode = Find(tree.Nodes);
    }
    bool EnsureVault()
    {
        if (store.IsVaultUnlocked) return true;
        if (!store.HasMasterPassword) return CreateMasterPassword();
        while (true)
        {
            var password = TextPrompt.AskWithSkip(this, "解锁密码库", "请输入主密码", secret: true);
            if (password == "") return false;
            if (password == null) return false;
            try { store.UnlockVault(password); return true; }
            catch (System.Security.Cryptography.CryptographicException) { MessageBox.Show(this, "主密码不正确，或密码库数据已损坏。", "无法解锁", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
    bool CreateMasterPassword()
    {
        while (true)
        {
            var password = TextPrompt.Ask(this, "设置主密码", "主密码至少 8 位；遗忘后无法恢复已保存的密码。", secret: true);
            if (password == null) return false;
            var confirmation = TextPrompt.Ask(this, "确认主密码", "请再次输入主密码", secret: true);
            if (confirmation == null) return false;
            if (password.Length < 8 || password != confirmation) { MessageBox.Show(this, "两次输入必须一致，且至少 8 个字符。"); continue; }
            try { store.SetMasterPassword(password); return true; }
            catch (Exception) { MessageBox.Show(this, "无法保存密码库或迁移旧密码。请检查配置目录权限，并在原 Windows 账户下重试；原配置不会被覆盖。", "密码库设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        }
    }
    void ConfigureMasterPassword()
    {
        if (store.HasMasterPassword)
        {
            var current = TextPrompt.Ask(this, "修改主密码", "请先输入当前主密码", secret: true);
            if (current == null) return;
            try { store.UnlockVault(current); }
            catch (System.Security.Cryptography.CryptographicException) { MessageBox.Show(this, "主密码不正确，或密码库数据已损坏。"); return; }
        }
        if (CreateMasterPassword()) status.Text = "主密码已设置；密码库已解锁";
    }
    void Edit(Session? session)
    {
        if (session == null) session = new Session { Group = tree.SelectedNode?.Tag as string ?? (tree.SelectedNode?.Parent?.Tag as string) ?? "默认" };
        string password = session is { SavePassword: true } && store.IsVaultUnlocked ? store.GetPassword(session.Id) : "";
        using var editor = new SessionEditor(session, password, store.Data.Groups.Concat(store.Data.Sessions.Select(s => s.Group)));
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        if (editor.Persist) { if (editor.Result.SavePassword && editor.Password.Length > 0 && !EnsureVault()) return; store.PutSession(editor.Result, editor.Password); RefreshTree(); }
        if (editor.ConnectNow) Connect(editor.Result, editor.Password);
    }
    void ConnectSelected()
    {
        if (Selected is not { } s) return;
        string password = "";
        if (s.SavePassword) { if (!store.IsVaultUnlocked) { MessageBox.Show(this, "当前未解锁主密码，请手动输入本次密码。", "密码库未解锁", MessageBoxButtons.OK, MessageBoxIcon.Information); } else password = store.GetPassword(s.Id); }
        if (password.Length == 0 && s.Protocol is Protocol.SSH or Protocol.SFTP or Protocol.FTP)
        {
            var answer = TextPrompt.Ask(this, s.Name, s.PrivateKey.Length > 0 ? "私钥口令（未加密私钥可留空）" : "密码（本次使用，不保存）", secret: true);
            if (answer == null) return; password = answer;
        }
        Connect(s, password);
    }
    bool Confirm(string message)
    {
        if (IsDisposed || !IsHandleCreated) return false;
        if (InvokeRequired) return (bool)Invoke(() => Confirm(message));
        return MessageBox.Show(this, message, "SSH 主机身份验证", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }
    void Connect(Session session, string password)
    {
        session.Validate();
        var page = new TabPage(session.Name) { BackColor = Color.White, Tag = session }; tabs.TabPages.Add(page); tabs.SelectedTab = page;
        if (session.Protocol is Protocol.FTP or Protocol.SFTP)
        {
            var files = new FileView(session, store, password, Confirm) { Dock = DockStyle.Fill }; connections[page] = files; page.Controls.Add(files);
        }
        else if (session.Protocol == Protocol.RDP)
        {
            try { RdpLauncher.Launch(session, store.DirectoryPath); RdpPage(page, session, password); }
            catch { tabs.TabPages.Remove(page); page.Dispose(); throw; }
        }
        else
        {
            var terminal = new TerminalView { Dock = DockStyle.Fill };
            var connection = new TerminalConnection(session, store, password, Confirm); connections[page] = connection;
            connection.Output += terminal.Append; terminal.Input += connection.Send; terminal.TerminalResized += (cols, rows) => { _ = Task.Run(() => connection.Resize(cols, rows)); };
            page.Controls.Add(terminal); terminal.Focus();
            _ = RunTerminal();
            async Task RunTerminal()
            {
                try { await connection.Run(terminal.Screen.Columns, terminal.Screen.Rows); }
                catch (Exception e) { terminal.Append("\r\n\x1b[31m[连接失败：" + e.Message + "]\x1b[0m\r\n"); }
            }
        }
    }
    void RdpPage(TabPage page, Session s, string password)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(35), FlowDirection = FlowDirection.TopDown };
        panel.Controls.Add(new Label { Text = "远程桌面已在 Windows 客户端中打开", Font = new Font(Font.FontFamily, 15, FontStyle.Bold), AutoSize = true });
        panel.Controls.Add(new Label { Text = $"{s.Host}:{s.Port}  ·  {s.Username}\n\n使用系统窗口完成登录。关闭此标签不会关闭远程桌面窗口。", AutoSize = true, Margin = new Padding(0, 18, 0, 20) });
        var open = new Button { Text = "再次打开远程桌面", AutoSize = true }; open.Click += (_, _) => Guard(() => RdpLauncher.Launch(s, store.DirectoryPath)); panel.Controls.Add(open);
        if (password.Length > 0)
        {
            var copy = new Button { Text = "复制会话密码（30 秒后清除）", AutoSize = true };
            var timer = new System.Windows.Forms.Timer { Interval = 30000 };
            copy.Click += (_, _) => Guard(() => { Clipboard.SetText(password); timer.Stop(); timer.Start(); });
            timer.Tick += (_, _) => { timer.Stop(); try { if (Clipboard.ContainsText() && Clipboard.GetText() == password) Clipboard.Clear(); } catch { } };
            page.Disposed += (_, _) => { timer.Dispose(); try { if (Clipboard.ContainsText() && Clipboard.GetText() == password) Clipboard.Clear(); } catch { } };
            panel.Controls.Add(copy);
        }
        page.Controls.Add(panel);
    }
    void Delete()
    {
        if (Selected is { } s && MessageBox.Show(this, "删除会话及已保存的密码？\n" + s.Name, "删除会话", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { store.Delete(s.Id); RefreshTree(); }
    }
    void CloseTab()
    {
        if (tabs.SelectedTab is not { } page) return;
        CloseTab(page);
    }
    internal void CloseTab(TabPage page, Func<bool>? confirm = null)
    {
        if (!tabs.TabPages.Contains(page)) return;
        if (!(confirm?.Invoke() ?? (MessageBox.Show(this, $"关闭标签“{page.Text}”？\n内嵌连接会断开，正在进行的文件操作将取消。\n外部远程桌面窗口不会随之关闭。", "确认关闭", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes))) return;
        if (connections.Remove(page, out var connection)) connection.Dispose();
        tabs.TabPages.Remove(page); page.Dispose();
        if (tabs.TabCount == 0) Welcome();
    }
    void Import()
    {
        using var dialog = new OpenFileDialog { Filter = "PortHarbor 会话 (*.json)|*.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (MessageBox.Show(this, "仅导入你信任的会话配置。Shell 会话可在连接时运行其中的命令。\n继续导入？", "导入会话", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int count = store.Import(dialog.FileName); RefreshTree(); status.Text = $"已导入 {count} 个会话（不含密码及主机信任记录）";
        }
    }
    void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "PortHarbor 会话 (*.json)|*.json", FileName = "portharbor-sessions.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) { store.Export(dialog.FileName); status.Text = "会话已导出，不包含密码及主机信任记录"; }
    }
    void Welcome()
    {
        var page = new TabPage("开始") { BackColor = Color.White };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(38, 40, 20, 20), AutoScroll = true, WrapContents = false };
        flow.Controls.Add(new Label { Text = "PortHarbor", Font = new Font(Font.FontFamily, 28, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(24, 41, 71) });
        flow.Controls.Add(new Label { Text = "一个轻量的连接工作台", Font = new Font(Font.FontFamily, 13), AutoSize = true, ForeColor = Color.SlateGray, Margin = new Padding(0, 6, 0, 30) });
        flow.Controls.Add(new Label { Text = "终端连接、文件传输和会话管理，集中在一个窗口。", AutoSize = true, Margin = new Padding(0, 0, 0, 24) });
        var choices = new FlowLayoutPanel { Width = 690, Height = 170, WrapContents = true };
        foreach (var p in Enum.GetValues<Protocol>())
        {
            var button = new Button { Text = p == Protocol.Shell ? "Shell\n本地命令行" : p == Protocol.Serial ? "Serial\n串口终端" : p == Protocol.RDP ? "RDP\n远程桌面" : p + "\n" + (p is Protocol.FTP or Protocol.SFTP ? "文件传输" : "远程终端"), Size = new Size(145, 66), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(245, 248, 253), ForeColor = Color.FromArgb(35, 65, 110), Margin = new Padding(0, 0, 12, 12) };
            button.FlatAppearance.BorderColor = Color.FromArgb(220, 228, 241);
            button.Click += (_, _) => Guard(() => Edit(new Session { Name = p == Protocol.Shell ? "本地 Shell" : "新 " + p + " 会话", Protocol = p, Port = Session.DefaultPort(p) })); choices.Controls.Add(button);
        }
        flow.Controls.Add(choices);
        flow.Controls.Add(new Label { Text = "从左侧会话库双击连接，或选择上方连接类型开始。\n\n保存密码需设置主密码；安装版另外使用 Windows 用户级加密。\n导出的会话不包含密码。Telnet 与普通 FTP 不加密流量。", AutoSize = true, ForeColor = Color.SlateGray, Margin = new Padding(0, 16, 0, 0) });
        page.Controls.Add(flow); tabs.TabPages.Add(page); tabs.SelectedTab = page;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) tree.ImageList?.Dispose();
        base.Dispose(disposing);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.N)) { Guard(() => Edit(null)); return true; }
        if (keyData == (Keys.Control | Keys.W)) { CloseTab(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}




