using System.Diagnostics;

namespace PortHarbor;

public sealed class MainForm : Form
{
    readonly Store store;
    readonly TreeView tree = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, FullRowSelect = true, ItemHeight = 32, ShowLines = false, ShowRootLines = false, BackColor = Color.FromArgb(242, 245, 250) };
    readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "搜索会话、主机或协议…", Margin = new Padding(8) };
    readonly TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 7) };
    readonly Dictionary<TabPage, IDisposable> connections = [];
    readonly ToolStripStatusLabel status = new() { Text = "就绪", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    public MainForm(Store store)
    {
        this.store = store;
        Text = "PortHarbor · 轻量连接工作台"; Size = new Size(1250, 820); MinimumSize = new Size(850, 560);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 9); AutoScaleMode = AutoScaleMode.Dpi; BackColor = Color.White;
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8, 7, 8, 7), BackColor = Color.White, Renderer = new ToolStripProfessionalRenderer() };
        Button("＋ 新建连接", () => Edit(null)); Button("连接", ConnectSelected); Button("编辑", () => { if (Selected is { } s) Edit(s); });
        Button("关闭标签", CloseTab); toolbar.Items.Add(new ToolStripSeparator());
        Button("导入会话", Import); Button("导出会话", Export); Button("配置目录", () => Process.Start(new ProcessStartInfo("explorer.exe", store.DirectoryPath) { UseShellExecute = true }));
        Button("关于", () => MessageBox.Show(this, "PortHarbor 0.1.0\nMIT 开源许可证\n\nSSH · Telnet · RDP · FTP · SFTP · Serial · Shell\n原生 WinForms 界面；UTF-8 基础 ANSI/VT 终端。\nRDP 使用 Windows 远程桌面客户端。\n\n源码、功能限制和第三方许可见发行包 README。", "关于 PortHarbor"));
        void Button(string title, Action action) { var item = new ToolStripButton(title) { DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(5, 3, 5, 3) }; item.Click += (_, _) => Guard(action); toolbar.Items.Add(item); }
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 1, BackColor = Color.FromArgb(220, 226, 235), Panel1MinSize = 190 };
        var sidebar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = tree.BackColor };
        var title = new Label { Dock = DockStyle.Top, Height = 43, Text = "会话库", Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Padding = new Padding(0, 8, 0, 0) };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 48, Text = "双击连接 · 右键管理\nCtrl+N 新建 · Ctrl+W 关闭", ForeColor = Color.SlateGray };
        sidebar.Controls.Add(tree); sidebar.Controls.Add(search); sidebar.Controls.Add(title); sidebar.Controls.Add(hint);
        split.Panel1.Controls.Add(sidebar); split.Panel2.Controls.Add(tabs);
        var bar = new StatusStrip(); bar.Items.Add(status); bar.Items.Add(new ToolStripStatusLabel("MIT · 0.1.0"));
        Controls.Add(split); Controls.Add(toolbar); Controls.Add(bar);
        Shown += (_, _) => { split.SplitterDistance = 250; };
        search.TextChanged += (_, _) => RefreshTree();
        tree.NodeMouseDoubleClick += (_, _) => Guard(ConnectSelected);
        tree.NodeMouseClick += (_, e) => tree.SelectedNode = e.Node;
        var context = new ContextMenuStrip();
        context.Items.Add("连接", null, (_, _) => Guard(ConnectSelected));
        context.Items.Add("编辑", null, (_, _) => { if (Selected is { } s) Guard(() => Edit(s)); });
        context.Items.Add("复制会话", null, (_, _) => { if (Selected is { } s) Guard(() => Edit(s with { Id = Guid.NewGuid(), Name = s.Name + " 副本", SavePassword = false })); });
        context.Items.Add("删除会话及其密码", null, (_, _) => Guard(Delete)); tree.ContextMenuStrip = context;
        tabs.MouseDown += (_, e) => { if (e.Button == MouseButtons.Middle) { for (int i = 0; i < tabs.TabCount; i++) if (tabs.GetTabRect(i).Contains(e.Location)) { tabs.SelectedIndex = i; CloseTab(); break; } } };
        RefreshTree(); Welcome();
        FormClosing += (_, _) => { foreach (var connection in connections.Values) connection.Dispose(); };
    }
    void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "PortHarbor", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    Session? Selected => tree.SelectedNode?.Tag as Session;
    void RefreshTree()
    {
        Guid? selected = Selected?.Id; string filter = search.Text.Trim(); tree.BeginUpdate(); tree.Nodes.Clear();
        foreach (var group in store.Data.Sessions.Where(s => filter.Length == 0 || $"{s.Name} {s.Host} {s.Protocol} {s.Group}".Contains(filter, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Group).ThenBy(s => s.Name).GroupBy(s => string.IsNullOrWhiteSpace(s.Group) ? "默认" : s.Group))
        {
            var folder = tree.Nodes.Add(group.Key);
            foreach (var s in group) { var node = folder.Nodes.Add($"{s.Name}  ·  {s.Protocol}"); node.Tag = s; node.ToolTipText = $"{s.Host}:{s.Port}"; if (s.Id == selected) tree.SelectedNode = node; }
            folder.Expand();
        }
        tree.EndUpdate(); status.Text = $"{store.Data.Sessions.Count} 个已保存会话";
    }
    void Edit(Session? session)
    {
        string password = session is { SavePassword: true } ? store.GetPassword(session.Id) : "";
        using var editor = new SessionEditor(session, password);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        if (editor.Persist) { store.PutSession(editor.Result, editor.Password); RefreshTree(); }
        if (editor.ConnectNow) Connect(editor.Result, editor.Password);
    }
    void ConnectSelected()
    {
        if (Selected is not { } s) return;
        string password = s.SavePassword ? store.GetPassword(s.Id) : "";
        if (!s.SavePassword && s.Protocol is Protocol.SSH or Protocol.SFTP or Protocol.FTP)
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
        var page = new TabPage(session.Name) { BackColor = Color.White }; tabs.TabPages.Add(page); tabs.SelectedTab = page;
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
        flow.Controls.Add(new Label { Text = "从左侧会话库双击连接，或选择上方连接类型开始。\n\n密码由 Windows 用户级加密保护。导出的会话不包含密码。\nTelnet 与普通 FTP 不加密流量，请按网络环境选择。", AutoSize = true, ForeColor = Color.SlateGray, Margin = new Padding(0, 16, 0, 0) });
        page.Controls.Add(flow); tabs.TabPages.Add(page); tabs.SelectedTab = page;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.N)) { Guard(() => Edit(null)); return true; }
        if (keyData == (Keys.Control | Keys.W)) { CloseTab(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
