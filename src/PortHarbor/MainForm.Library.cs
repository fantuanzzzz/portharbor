namespace PortHarbor;

public sealed partial class MainForm
{
    readonly SessionActivityService sessionActivity;
    readonly ToolStrip libraryFilters = new() { Name = "SessionLibraryFilters", AccessibleName = "会话库视图", TabStop = true, Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, CanOverflow = true };
    readonly Label libraryNotice = new() { Name = "LibraryNotice", Dock = DockStyle.Top, Height = 46, AutoEllipsis = true, Padding = new Padding(0, 5, 0, 0) };
    readonly UiButton clearSearch = new() { Name = "ClearSessionSearch", Text = "清空", MinimumSize = new Size(64, UiTheme.ButtonHeight), Width = 64, Padding = new Padding(6, 4, 6, 4), AccessibleName = "清空会话搜索" };
    readonly ToolStripStatusLabel libraryMessage = new() { Name = "LibraryMessage" };
    readonly Dictionary<SessionLibraryView, ToolStripButton> libraryViewItems = [];
    readonly List<ToolStripMenuItem> favoriteItems = [], renameItems = [], clearRecentItems = [], clearFavoriteItems = [];
    SessionLibraryView libraryView;

    Control CreateLibrarySearch()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        search.Dock = DockStyle.Top; search.Margin = new Padding(0, 6, 4, 0); search.AccessibleName = "搜索会话、主机、协议或分类";
        clearSearch.Margin = new Padding(0); clearSearch.Dock = DockStyle.Fill;
        panel.Controls.Add(search, 0, 0); panel.Controls.Add(clearSearch, 1, 0);
        clearSearch.Click += (_, _) => { search.Clear(); search.Focus(); };
        search.KeyDown += (_, e) =>
        {
            if (e.Modifiers != Keys.None) return;
            if (e.KeyCode == Keys.Escape) { search.Clear(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode is Keys.Down or Keys.Up or Keys.Enter)
            {
                var nodes = LibrarySessionNodes().ToArray();
                if (nodes.Length > 0)
                {
                    tree.SelectedNode = e.KeyCode == Keys.Up ? nodes[^1] : nodes[0]; tree.SelectedNode.EnsureVisible(); tree.Focus();
                    if (e.KeyCode == Keys.Enter) Guard(ConnectSelected);
                }
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        foreach (var view in Enum.GetValues<SessionLibraryView>())
        {
            var button = new ToolStripButton(view == SessionLibraryView.All ? "全部" : view == SessionLibraryView.Favorites ? "收藏" : "最近")
            { Name = "Library" + view, DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = view == SessionLibraryView.Recent ? "最近 20 个已保存会话的连接尝试，时间不代表连接成功" : "显示" + (view == SessionLibraryView.All ? "全部会话" : "收藏会话") };
            button.Click += (_, _) => SetLibraryView(view); libraryFilters.Items.Add(button); libraryViewItems[view] = button;
        }
        UiTheme.Role(libraryNotice, UiRole.Muted);
        return panel;
    }
    void AddLibraryMenuItems(ToolStripItemCollection items)
    {
        ToolStripMenuItem Add(string name, string text, Action action)
        {
            var item = new ToolStripMenuItem(text) { Name = name }; item.Click += (_, _) => Guard(action); items.Add(item); return item;
        }
        items.Add(new ToolStripSeparator());
        favoriteItems.Add(Add("ToggleFavorite", "收藏此会话", ToggleSelectedFavorite));
        renameItems.Add(Add("RenameSession", "重命名会话…", RenameSelected));
        clearRecentItems.Add(Add("ClearRecent", "清空最近连接…", () => ClearLibraryHistory(false)));
        clearFavoriteItems.Add(Add("ClearFavorites", "清空收藏…", () => ClearLibraryHistory(true)));
    }
    void InitializeSessionLibrary()
    {
        ((StatusStrip)status.Owner!).Items.Insert(1, libraryMessage);
        libraryMessage.TextChanged += (_, _) => libraryMessage.ForeColor = UiTheme.Palette(this).Error;
        if (sessionActivity.LoadFailed) libraryMessage.Text = "收藏/最近记录未能读取，原文件已保留；请检查 session-activity.json。";
    }
    IEnumerable<TreeNode> LibrarySessionNodes()
    {
        IEnumerable<TreeNode> Walk(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes) { if (node.Tag is Session) yield return node; foreach (var child in Walk(node.Nodes)) yield return child; }
        }
        return Walk(tree.Nodes);
    }
    internal void SetLibraryView(SessionLibraryView view)
    {
        if (!Enum.IsDefined(view)) throw new ArgumentException("会话视图无效。");
        libraryView = view; RefreshTree();
    }
    void UpdateLibraryActions()
    {
        bool selected = Selected != null, favorite = selected && sessionActivity.Current.Favorites.Contains(Selected!.Id);
        foreach (var item in favoriteItems) { item.Enabled = selected; item.Text = favorite ? "取消收藏" : "收藏此会话"; item.Checked = favorite; }
        foreach (var item in renameItems) item.Enabled = selected;
        foreach (var item in clearRecentItems) item.Enabled = sessionActivity.Current.Recent.Length > 0;
        foreach (var item in clearFavoriteItems) item.Enabled = sessionActivity.Current.Favorites.Length > 0;
        foreach (var (view, item) in libraryViewItems) item.Checked = view == libraryView;
        clearSearch.Enabled = search.Text.Length > 0;
    }
    bool TrySaveLibrary(Action action)
    {
        try { action(); libraryMessage.Text = ""; return true; }
        catch (Exception error) when (SessionActivityService.IsStorageFailure(error))
        { libraryMessage.Text = "收藏/最近记录未保存；请检查 session-activity.json 或目录权限，原文件已保留。"; return false; }
    }
    internal void ToggleSelectedFavorite()
    {
        if (Selected is not { } session) return;
        bool added = false;
        if (!TrySaveLibrary(() => added = sessionActivity.ToggleFavorite(session.Id, configuration.GetSessions().Select(s => s.Id)))) return;
        RefreshTree(); status.Text = added ? $"已收藏：{session.Name}" : $"已取消收藏：{session.Name}";
    }
    void RecordConnectionAttempt(Session session)
    {
        if (!rememberLayout) return; // Startup smoke must not write user activity into distributable files.
        var ids = configuration.GetSessions().Select(s => s.Id).ToArray();
        if (!ids.Contains(session.Id)) return;
        bool recorded = false;
        if (TrySaveLibrary(() => recorded = sessionActivity.RecordAttempt(session.Id, ids, DateTimeOffset.UtcNow)) && recorded) RefreshTree();
    }
    internal void ClearLibraryHistory(bool favorites, Func<bool>? confirm = null)
    {
        if (!(confirm?.Invoke() ?? MessageBox.Show(this, favorites ? "清空所有收藏？会话和密码会保留。" : "清空最近连接记录？会话、收藏和密码会保留。", "会话库", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)) return;
        var ids = configuration.GetSessions().Select(s => s.Id).ToArray();
        if (!TrySaveLibrary(() => { if (favorites) sessionActivity.ClearFavorites(ids); else sessionActivity.ClearRecent(ids); })) return;
        RefreshTree(); status.Text = favorites ? "收藏已清空，会话和密码已保留" : "最近连接记录已清空，会话和密码已保留";
    }
    void RenameSelected()
    {
        if (Selected is not { } session) return;
        string? name = TextPrompt.Ask(this, "重命名会话", "新会话名称", session.Name);
        if (name == null) return;
        session = configuration.GetSessions().FirstOrDefault(s => s.Id == session.Id);
        if (session == null) { RefreshTree(); status.Text = "此会话已不存在，未重命名"; return; }
        if (!configuration.IsVaultUnlocked && session.SavePassword)
        {
            if (!EnsureVault()) { status.Text = "未重命名：请先解锁密码库，以保留会话密码"; return; }
        }
        string password = session.SavePassword ? configuration.GetPassword(session.Id) : "";
        var renamed = session with { Name = name.Trim() }; configuration.SaveSession(renamed, password);
        RevealSavedSession(renamed.Id); status.Text = $"已重命名：{renamed.Name}";
    }
    void RevealSavedSession(Guid id)
    {
        libraryView = SessionLibraryView.All; search.Clear(); RefreshTree();
        tree.SelectedNode = LibrarySessionNodes().FirstOrDefault(n => n.Tag is Session s && s.Id == id); tree.SelectedNode?.EnsureVisible();
    }
    internal bool DeleteSelectedSession(Func<bool> confirm)
    {
        if (Selected is not { } session || !confirm()) return false;
        configuration.DeleteSession(session.Id);
        bool cleaned = TrySaveLibrary(() =>
        {
            if (sessionActivity.LoadFailed) throw new InvalidDataException();
            sessionActivity.Reconcile(configuration.GetSessions().Select(s => s.Id));
        });
        RefreshTree(); status.Text = cleaned ? $"已删除会话及其密码：{session.Name}" : "会话及密码已删除；收藏/最近记录清理未保存，重启后将重试";
        return true;
    }
}
