namespace PortHarbor;

public sealed partial class MainForm
{
    readonly WorkspaceLayoutService workspaceLayout;
    readonly bool rememberLayout;
    readonly SplitContainer workspaceSplit = new()
    {
        Name = "WorkspaceSplit", Size = new Size(1000, 600), Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1,
        SplitterWidth = 5, BackColor = Color.FromArgb(220, 226, 235), Panel1MinSize = 190, Panel2MinSize = 300
    };
    readonly ToolStrip actions = new() { Name = "WorkspaceActions", Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden,
        CanOverflow = true, TabStop = true, Padding = new Padding(8, 4, 8, 4) };
    readonly ToolStripStatusLabel layoutMessage = new() { Name = "LayoutMessage" };
    readonly System.Windows.Forms.Timer layoutSaveTimer = new() { Interval = 750 };
    ToolStripButton connectAction = null!, editAction = null!, sidebarAction = null!;
    Rectangle normalBounds;
    bool workspaceReady, applyingWorkspace, lastMaximized, sidebarVisible = true;
    int preferredSidebarWidth = 250;
    InterfaceTheme interfaceTheme;
    readonly Dictionary<InterfaceTheme, ToolStripMenuItem> themeItems = [];

    ToolStrip CreateWorkspaceActions()
    {
        ToolStripButton Add(string name, string text, string hint, Action action)
        {
            var button = new ToolStripButton(text) { Name = name, DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = hint, Overflow = ToolStripItemOverflow.AsNeeded };
            button.Click += (_, _) => Guard(action); actions.Items.Add(button); return button;
        }
        Add("NewConnection", "新建连接", "新建连接（Ctrl+N）", () => Edit(null));
        connectAction = Add("ConnectSelected", "连接", "连接已选会话（会话树 Enter）", ConnectSelected);
        editAction = Add("EditSelected", "编辑", "编辑已选会话（会话树 F2）", () => { if (Selected is { } session) Edit(session); });
        actions.Items.Add(new ToolStripSeparator());
        Add("SearchSessions", "搜索会话", "显示会话库并搜索（Ctrl+Shift+F）", FocusSessionSearch);
        sidebarAction = Add("ToggleSidebar", "会话库", "显示/隐藏会话库（Ctrl+Shift+B）", ToggleSidebar);
        return actions;
    }
    void InitializeWorkspace()
    {
        search.Name = "SessionSearch"; search.TabIndex = 0; tree.TabIndex = 1; tabs.TabIndex = 0;
        workspaceSplit.TabIndex = 1; actions.TabIndex = 0;
        ((StatusStrip)status.Owner!).Items.Insert(1, layoutMessage);
        tree.AfterSelect += (_, _) => UpdateWorkspaceActions();
        tree.KeyDown += (_, e) =>
        {
            if (e.Modifiers != Keys.None) return;
            if (e.KeyCode == Keys.Enter) { Guard(ConnectSelected); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F2 && Selected is { } session) { Guard(() => Edit(session)); e.Handled = e.SuppressKeyPress = true; }
        };
        Load += (_, _) => ApplyWorkspaceLayout(workspaceLayout.Current);
        Shown += (_, _) => { workspaceReady = true; TrackWorkspaceChange(); };
        Move += (_, _) => TrackWorkspaceChange(); Resize += (_, _) => TrackWorkspaceChange();
        workspaceSplit.SizeChanged += (_, _) => ApplySidebarWidth();
        workspaceSplit.SplitterMoved += (_, _) =>
        {
            if (!workspaceReady || applyingWorkspace || workspaceSplit.Panel1Collapsed) return;
            preferredSidebarWidth = (int)Math.Round(workspaceSplit.SplitterDistance * 96d / DeviceDpi);
            QueueWorkspaceSave();
        };
        layoutSaveTimer.Tick += (_, _) => SaveWorkspaceLayout();
        layoutMessage.TextChanged += (_, _) => layoutMessage.ForeColor = UiTheme.Palette(this).Error;
    }
    void UpdateWorkspaceActions()
    {
        connectAction.Enabled = editAction.Enabled = Selected != null;
        sidebarAction.Checked = sidebarVisible;
        UpdateLibraryActions();
    }
    void ApplyWorkspaceLayout(WorkspaceLayout value)
    {
        applyingWorkspace = true;
        try
        {
            var screens = Screen.AllScreens.OrderByDescending(s => s.Primary).Select(s => s.WorkingArea).ToArray();
            var minimum = new Size((int)Math.Round(850 * DeviceDpi / 96d), (int)Math.Round(560 * DeviceDpi / 96d));
            var bounds = WorkspacePlacement.Restore(value, screens, DeviceDpi, minimum);
            var area = screens.OrderByDescending(r => (long)Rectangle.Intersect(r, bounds).Width * Rectangle.Intersect(r, bounds).Height).First();
            MinimumSize = new Size(Math.Min(minimum.Width, area.Width), Math.Min(minimum.Height, area.Height));
            WindowState = FormWindowState.Normal; StartPosition = FormStartPosition.Manual; Bounds = normalBounds = bounds;
            preferredSidebarWidth = value.SidebarWidth; sidebarVisible = value.SidebarVisible;
            interfaceTheme = value.Theme; ApplyInterfaceTheme();
            workspaceSplit.Panel1Collapsed = !sidebarVisible;
            ApplySidebarWidth();
            lastMaximized = value.Maximized;
            if (value.Maximized) WindowState = FormWindowState.Maximized;
            UpdateWorkspaceActions();
        }
        finally { applyingWorkspace = false; }
    }
    void ApplySidebarWidth()
    {
        if (workspaceSplit.Panel1Collapsed) return;
        bool previous = applyingWorkspace; applyingWorkspace = true;
        try
        {
            int requested = (int)Math.Round(preferredSidebarWidth * DeviceDpi / 96d);
            int maximum = workspaceSplit.ClientSize.Width - workspaceSplit.SplitterWidth - workspaceSplit.Panel2MinSize;
            if (maximum >= workspaceSplit.Panel1MinSize)
                workspaceSplit.SplitterDistance = Math.Clamp(requested, workspaceSplit.Panel1MinSize, maximum);
        }
        finally { applyingWorkspace = previous; }
    }
    void TrackWorkspaceChange()
    {
        if (!workspaceReady || applyingWorkspace) return;
        if (WindowState == FormWindowState.Normal) normalBounds = Bounds;
        if (WindowState != FormWindowState.Minimized) lastMaximized = WindowState == FormWindowState.Maximized;
        QueueWorkspaceSave();
    }
    void QueueWorkspaceSave()
    {
        if (!rememberLayout || !workspaceReady || applyingWorkspace || shutdownPending) return;
        layoutSaveTimer.Stop(); layoutSaveTimer.Start();
    }
    internal bool SaveWorkspaceLayout()
    {
        layoutSaveTimer.Stop();
        if (!rememberLayout || !workspaceReady) return true;
        var value = new WorkspaceLayout { X = normalBounds.X, Y = normalBounds.Y, Width = normalBounds.Width,
            Height = normalBounds.Height, Dpi = DeviceDpi, HasPosition = true, Maximized = lastMaximized,
            SidebarWidth = preferredSidebarWidth, SidebarVisible = sidebarVisible, Theme = interfaceTheme };
        try
        {
            if (workspaceLayout.LoadFailed || workspaceLayout.Current != value) workspaceLayout.Save(value);
            layoutMessage.Text = ""; return true;
        }
        catch (Exception e) when (WorkspaceLayoutService.IsStorageFailure(e))
        { layoutMessage.Text = "布局未保存；请检查配置目录权限或 workspace-layout.json，原文件已保留。"; return false; }
    }
    internal void ResetWorkspaceLayout()
    {
        ApplyWorkspaceLayout(new WorkspaceLayout { Theme = interfaceTheme }); SaveWorkspaceLayout();
    }
    internal void SetInterfaceTheme(InterfaceTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentException("界面主题无效。");
        interfaceTheme = theme; ApplyInterfaceTheme(); SaveWorkspaceLayout();
    }
    void ApplyInterfaceTheme()
    {
        UiTheme.Apply(this, interfaceTheme);
        foreach (var (choice, item) in themeItems) item.Checked = choice == interfaceTheme;
        var previous = tree.ImageList;
        tree.ImageList = SessionTreeIcons.Create(UiTheme.Palette(this), DeviceDpi);
        previous?.Dispose();
        layoutMessage.ForeColor = UiTheme.Palette(this).Error;
        libraryMessage.ForeColor = UiTheme.Palette(this).Error;
    }
    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        if (themeItems.Count > 0 && tree.ImageList != null) ApplyInterfaceTheme();
    }
    void ToggleSidebar()
    {
        sidebarVisible = !sidebarVisible;
        workspaceSplit.Panel1Collapsed = !sidebarVisible; ApplySidebarWidth(); UpdateWorkspaceActions();
        if (!sidebarVisible) FocusWorkspace();
        QueueWorkspaceSave();
    }
    void FocusSessionSearch()
    {
        if (!sidebarVisible) ToggleSidebar();
        search.Focus(); search.SelectAll();
    }
    void FocusWorkspace()
    {
        if (tabs.SelectedTab?.Controls.OfType<TerminalView>().FirstOrDefault() is { } terminal) terminal.Focus();
        else if (tabs.SelectedTab is not { } page || !page.SelectNextControl(null, true, true, true, false)) tabs.Focus();
    }
    internal bool HandleWorkspaceKey(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.F)) { FocusSessionSearch(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.B)) { ToggleSidebar(); return true; }
        if (keyData is not (Keys.F6 or (Keys.Shift | Keys.F6))) return false;
        if (!sidebarVisible) { FocusWorkspace(); return true; }
        bool reverse = keyData.HasFlag(Keys.Shift);
        if (search.Focused) { if (reverse) FocusWorkspace(); else tree.Focus(); }
        else if (tree.Focused) { if (reverse) search.Focus(); else FocusWorkspace(); }
        else { if (reverse) tree.Focus(); else FocusSessionSearch(); }
        return true;
    }
}
