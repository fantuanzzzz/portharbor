using System.Drawing;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void SessionActivityStorage(string root)
    {
        foreach (bool portable in new[] { false, true })
        {
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "activity-storage-" + portable), portable));
            var session = new Session { Host = "private-host.invalid", Name = "private-name", Username = "private-user", PrivateKey = "private-key" };
            store.PutSession(session, ""); var configuration = File.ReadAllBytes(store.FilePath);
            string path = Path.Combine(store.DirectoryPath, "session-activity.json"); var repo = new SessionActivityRepository(store.Paths);
            var service = new SessionActivityService(repo); service.Load();
            Check(!service.LoadFailed && !File.Exists(path), $"{portable} absent activity data defaults without creating a file");
            service.ToggleFavorite(session.Id, [session.Id]); service.RecordAttempt(session.Id, [session.Id], DateTimeOffset.UtcNow);
            var restarted = new SessionActivityService(repo); restarted.Load();
            Check(restarted.Current.Favorites.SequenceEqual([session.Id]) && restarted.Current.Recent.Single().SessionId == session.Id
                && configuration.SequenceEqual(File.ReadAllBytes(store.FilePath)), $"{portable} favorites and recent attempts persist independently of session configuration");
            string json = File.ReadAllText(path);
            Check(new[] { session.Host, session.Name, session.Username, session.PrivateKey, "Password", "Fingerprint" }.All(value => !json.Contains(value)),
                $"{portable} activity storage contains identities and times without session metadata or secrets");
            byte[] bytes = File.ReadAllBytes(path);
            using (var occupied = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false; try { restarted.ClearRecent([session.Id]); } catch (IOException) { failed = true; }
                Check(failed && restarted.Current.Recent.Length == 1 && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    $"{portable} failed activity save preserves current state and disk data");
            }
            restarted.ClearRecent([session.Id]);
            Check(File.ReadAllBytes(path + ".bak").SequenceEqual(bytes) && !Directory.GetFiles(store.DirectoryPath, "*.tmp").Any(),
                $"{portable} activity replacement retains its backup and cleans temporary files");
            string export = Path.Combine(root, "activity-export-" + portable + ".json"); store.Export(export);
            Check(!File.ReadAllText(export).Contains("Favorites") && !File.ReadAllText(export).Contains("AttemptedAt"),
                $"{portable} session export excludes favorites and recent history");
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"Activity\":{}}"); restarted.Load();
            Check(!restarted.LoadFailed && restarted.Current.Favorites.Length == 0 && restarted.Current.Recent.Length == 0,
                $"{portable} missing activity fields use empty defaults");
            foreach (string invalid in new[] { "{broken", "{\"SchemaVersion\":2,\"Activity\":{}}", "{\"SchemaVersion\":1,\"Activity\":null}",
                "{\"SchemaVersion\":1,\"Activity\":{\"Favorites\":null}}", "{\"SchemaVersion\":1,\"Activity\":{\"Recent\":[null]}}" })
            {
                File.WriteAllText(path, invalid); restarted.Load(); bool refused = false;
                try { restarted.ToggleFavorite(session.Id, [session.Id]); } catch (Exception error) when (SessionActivityService.IsStorageFailure(error)) { refused = true; }
                Check(restarted.LoadFailed && refused && File.ReadAllText(path) == invalid,
                    $"{portable} broken future and invalid activity data are preserved and cannot be overwritten");
            }
        }
    }

    static void SessionLibraryUi(string root, string output)
    {
        Directory.CreateDirectory(output);
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "library-ui"), true));
        var a = new Session { Name = "核心交换机", Host = "core.invalid", Protocol = Protocol.Telnet, Port = 23, Group = "工作/交换机/核心" };
        var b = new Session { Name = "备用设备", Host = "backup.invalid", Protocol = Protocol.Telnet, Port = 23, Group = "实验室" };
        store.PutSession(a, ""); store.PutSession(b, "");
        var opened = new List<(Session Session, string Password)>();
        MainForm Create(ISessionActivityRepository? repository = null) => new(store, new ConfigurationService(store, store, store),
            (session, password, _) => { opened.Add((session, password)); return new UnreportedTerminalFailure(); }, activityRepository: repository);
        IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes) { yield return node; foreach (var child in Nodes(node.Nodes)) yield return child; }
        }
        TreeNode Node(MainForm form, Guid id) => Nodes(FindControl<TreeView>(form).Nodes).Single(n => n.Tag is Session s && s.Id == id);
        void Select(MainForm form, Guid id) { var tree = FindControl<TreeView>(form); tree.SelectedNode = Node(form, id); }
        Label Notice(MainForm form) => Descendants(form).OfType<Label>().Single(c => c.Name == "LibraryNotice");
        ToolStrip Filters(MainForm form) => Descendants(form).OfType<ToolStrip>().Single(c => c.Name == "SessionLibraryFilters");
        void Snapshot(Form form, string name)
        {
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(output, name + ".png"));
        }
        void Close(MainForm form)
        {
            foreach (TabPage page in FindControl<TabControl>(form).TabPages) page.Tag = null;
            form.Close(); PumpUntil(() => form.IsDisposed);
        }
        var repository = new SessionActivityRepository(store.Paths);
        using (var form = Create())
        {
            form.Show(); Application.DoEvents(); Select(form, a.Id);
            form.Width = form.MinimumSize.Width; Application.DoEvents();
            var clear = Descendants(form).OfType<Button>().Single(c => c.Name == "ClearSessionSearch");
            Check(clear.Width - clear.Padding.Horizontal >= TextRenderer.MeasureText(clear.Text, clear.Font, Size.Empty, TextFormatFlags.NoPadding).Width,
                "clear-search label fits on one line at the minimum workspace width");
            form.Width = 1250; Application.DoEvents();
            form.ToggleSelectedFavorite();
            Check(Node(form, a.Id).Text.StartsWith("★") && repository.Load().Favorites.SequenceEqual([a.Id]),
                "favorite action persists the selected identity and displays its star marker");
            Filters(form).Items["LibraryFavorites"]!.PerformClick();
            Check(Nodes(FindControl<TreeView>(form).Nodes).Count(n => n.Tag is Session) == 1 && Node(form, a.Id).Parent!.Tag as string == a.Group,
                "favorites view shows only saved favorites while preserving nested category paths");
            Snapshot(form, "library-favorites");
            var search = FindControl<TextBox>(form); search.Text = "unmatched-query"; Application.DoEvents();
            Check(!Nodes(FindControl<TreeView>(form).Nodes).Any(n => n.Tag is Session) && Notice(form).Text.Contains("没有匹配")
                && !form.Controls.OfType<ToolStrip>().Single(t => t.Name == "WorkspaceActions").Items["ConnectSelected"]!.Enabled,
                "unmatched search shows feedback and disables connecting a stale selection");
            Snapshot(form, "library-no-results");
            Descendants(form).OfType<Button>().Single(c => c.Name == "ClearSessionSearch").PerformClick();
            Check(search.Text == "" && search.Focused && Node(form, a.Id) != null, "clear search restores the current view and keeps input focus");
            search.Text = "core.invalid"; search.Focus(); SendTabMessage(search.Handle, 0x100, new IntPtr((int)Keys.Down), IntPtr.Zero); Application.DoEvents();
            Check(FindControl<TreeView>(form).Focused && FindControl<TreeView>(form).SelectedNode?.Tag is Session first && first.Id == a.Id,
                "Down from search selects the first matching session through category folders");
            search.Focus(); SendTabMessage(search.Handle, 0x100, new IntPtr((int)Keys.Enter), IntPtr.Zero); Application.DoEvents();
            Check(opened.Last().Session.Id == a.Id && repository.Load().Recent.Single().SessionId == a.Id,
                "Enter from search follows the saved-session connection path and records its attempt");
            search.Focus(); SendTabMessage(search.Handle, 0x100, new IntPtr((int)Keys.Escape), IntPtr.Zero); Application.DoEvents();
            Check(search.Text.Length == 0, "Escape clears session search without closing the main window");
            form.Connect(b, ""); form.Connect(a, ""); form.Connect(new Session { Protocol = Protocol.Telnet, Host = "temporary.invalid", Port = 23 }, "one-time-secret"); Application.DoEvents();
            Filters(form).Items["LibraryRecent"]!.PerformClick();
            var recentNodes = FindControl<TreeView>(form).Nodes.Cast<TreeNode>().ToArray();
            Check(recentNodes.Select(n => ((Session)n.Tag!).Id).SequenceEqual([a.Id, b.Id]) && recentNodes[0].Text.Contains(a.Group)
                && recentNodes[0].ToolTipText.Contains("不代表连接成功"), "recent view orders distinct saved attempts and shows category and attempt semantics");
            Check(!File.ReadAllText(Path.Combine(store.DirectoryPath, "session-activity.json")).Contains("one-time-secret"),
                "temporary connection credentials never enter session activity storage");
            Snapshot(form, "library-recent");
            form.SetInterfaceTheme(InterfaceTheme.Dark); Snapshot(form, "library-recent-dark");
            form.ClearLibraryHistory(false, () => false);
            Check(repository.Load().Recent.Length == 2, "declining recent-history cleanup preserves persisted records");
            Select(form, a.Id); Check(!form.DeleteSelectedSession(() => false) && store.GetSessions().Count == 2,
                "declining session deletion retains sessions favorites and history");
            Check(form.DeleteSelectedSession(() => true) && repository.Load().Favorites.Length == 0 && repository.Load().Recent.Single().SessionId == b.Id,
                "confirmed session deletion removes its favorite and recent references");
            form.ClearLibraryHistory(false, () => true);
            Check(repository.Load().Recent.Length == 0 && Notice(form).Text.Contains("暂无最近") && store.GetSessions().Single().Id == b.Id,
                "clearing recent history displays an empty state and preserves saved sessions");
            Close(form);
        }
        // Restarts must resolve saved identities against current metadata and retry dangling-reference cleanup.
        var service = new SessionActivityService(repository); service.Load(); service.ToggleFavorite(b.Id, [b.Id]); service.RecordAttempt(b.Id, [b.Id], DateTimeOffset.UtcNow);
        store.PutSession(b with { Name = "更新后的设备" }, "");
        using (var restarted = Create())
        {
            restarted.Show(); Application.DoEvents(); restarted.SetLibraryView(SessionLibraryView.Favorites);
            Check(Node(restarted, b.Id).Text.Contains("更新后的设备") && repository.Load().Recent.Length == 1,
                "restarted favorites resolve current metadata while retaining recent history");
            restarted.ClearLibraryHistory(true, () => true);
            Check(repository.Load().Favorites.Length == 0 && repository.Load().Recent.Length == 1 && Notice(restarted).Text.Contains("暂无收藏"),
                "clearing favorites preserves recent history and shows its empty state");
            Close(restarted);
        }
        var failing = new SessionActivityChecks.Repository { Value = new() { Favorites = [b.Id], Recent = [new(b.Id, DateTimeOffset.UtcNow)] }, FailSave = true };
        using (var form = Create(failing))
        {
            form.Show(); Application.DoEvents(); Select(form, b.Id); form.ToggleSelectedFavorite();
            var warning = form.Controls.OfType<StatusStrip>().Single().Items["LibraryMessage"]!;
            Check(Node(form, b.Id).Text.StartsWith("★") && warning.Text!.Contains("未保存"), "favorite write failure preserves the visible saved state and reports failure");
            form.Connect(b, ""); Application.DoEvents();
            Check(opened.Last().Session.Id == b.Id && warning.Text!.Contains("未保存"), "history write failure does not prevent an explicit connection attempt");
            Select(form, b.Id); form.DeleteSelectedSession(() => true); form.SetLibraryView(SessionLibraryView.Favorites);
            Check(!Nodes(FindControl<TreeView>(form).Nodes).Any(n => n.Tag is Session) && failing.Value.Favorites.Contains(b.Id) && warning.Text!.Contains("未保存"),
                "failed reference cleanup cannot resurrect deleted sessions in favorites");
            Close(form);
        }
        failing.FailSave = false;
        using (var restarted = Create(failing))
        {
            restarted.Show(); Application.DoEvents();
            Check(failing.Value.Favorites.Length == 0 && failing.Value.Recent.Length == 0, "restart retries cleanup of references left by a failed deletion save");
            Close(restarted);
        }
        SessionLibraryCredentialUi(root, output);
    }

    static void SessionLibraryCredentialUi(string root, string output)
    {
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "library-credentials"), true));
        store.SetMasterPassword("fixture-master-password");
        var saved = new Session { Name = "密码会话", Host = "original.invalid", SavePassword = true, Group = "工作/安全" };
        store.PutSession(saved, "original-password");
        var opened = new List<(Session Session, string Password)>();
        using var form = new MainForm(store, new ConfigurationService(store, store, store),
            (session, password, _) => { opened.Add((session, password)); return new UnreportedTerminalFailure(); });
        form.Show(); Application.DoEvents();
        var tree = FindControl<TreeView>(form);
        IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes)
        { foreach (TreeNode node in nodes) { yield return node; foreach (var child in Nodes(node.Nodes)) yield return child; } }
        void Select() => tree.SelectedNode = Nodes(tree.Nodes).Single(n => n.Tag is Session s && s.Id == saved.Id);
        void Connect() => form.Controls.OfType<ToolStrip>().Single(t => t.Name == "WorkspaceActions").Items["ConnectSelected"]!.PerformClick();
        var repo = new SessionActivityRepository(store.Paths);
        Select(); form.ToggleSelectedFavorite(); form.SetLibraryView(SessionLibraryView.Favorites); Select(); Connect(); Application.DoEvents();
        Check(opened.Last().Password == "original-password" && opened.Last().Session.Id == saved.Id,
            "connecting from favorites obtains the current credential through the existing unlocked vault");
        form.SetLibraryView(SessionLibraryView.Recent); Select();
        store.PutSession(saved with { Host = "updated.invalid" }, "updated-password");
        Connect(); Application.DoEvents();
        Check(opened.Last().Password == "updated-password" && opened.Last().Session.Host == "updated.invalid",
            "connecting from recent history refreshes both saved session metadata and its vault credential");
        void Interact(Action open, Action<Form> action)
        {
            Exception? failure = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (_, _) =>
            {
                timer.Stop(); var dialog = Application.OpenForms.Cast<Form>().Single(f => f.Modal);
                try { action(dialog); } catch (Exception error) { failure = error; }
                finally { if (!dialog.IsDisposed) dialog.Close(); }
            };
            timer.Start(); open(); if (failure != null) throw failure;
        }
        var sessionsMenu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(m => m.Text == "会话");
        Interact(() => sessionsMenu.DropDownItems["RenameSession"]!.PerformClick(), dialog => dialog.DialogResult = DialogResult.Cancel);
        Check(store.GetSessions().Single().Name == saved.Name && repo.Load().Favorites.Contains(saved.Id),
            "cancelling rename retains the saved session and its activity associations");
        Interact(() => sessionsMenu.DropDownItems["RenameSession"]!.PerformClick(), dialog =>
        {
            FindControl<TextBox>(dialog).Text = "已重命名会话"; ((Button)dialog.AcceptButton!).PerformClick();
        });
        Check(store.GetSessions().Single().Name == "已重命名会话" && store.GetPassword(saved.Id) == "updated-password"
            && repo.Load().Favorites.Contains(saved.Id) && repo.Load().Recent.Single().SessionId == saved.Id,
            "rename keeps session identity saved credentials favorites and recent history");
        Select();
        Interact(() => tree.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(i => i.Text == "复制会话").PerformClick(), dialog =>
        {
            var editor = (SessionEditor)dialog;
            Check(editor.Result.Id != saved.Id && !editor.Result.SavePassword && editor.Password == "",
                "copy editor begins with a fresh identity and no stored password");
            Descendants(editor).OfType<Button>().Single(b => b.Text == "仅保存").PerformClick();
        });
        var copy = store.GetSessions().Single(s => s.Id != saved.Id);
        Check(!copy.SavePassword && store.GetPassword(copy.Id) == "" && !repo.Load().Favorites.Contains(copy.Id)
            && repo.Load().Recent.All(r => r.SessionId != copy.Id) && tree.SelectedNode?.Tag is Session selectedCopy && selectedCopy.Id == copy.Id,
            "saving a copy selects the new session without copying activity or credentials");
        // Cancelling a password prompt must not create a recent attempt or a transport.
        var unsecured = new Session { Name = "临时输入检查", Host = "password-prompt.invalid" }; store.PutSession(unsecured, "");
        form.SetLibraryView(SessionLibraryView.All); tree.SelectedNode = Nodes(tree.Nodes).Single(n => n.Tag is Session s && s.Id == unsecured.Id);
        int count = opened.Count;
        Interact(Connect, dialog => dialog.DialogResult = DialogResult.Cancel);
        Check(opened.Count == count && repo.Load().Recent.All(r => r.SessionId != unsecured.Id),
            "cancelling a one-time password prompt starts no connection and records no attempt");
        string json = File.ReadAllText(Path.Combine(store.DirectoryPath, "session-activity.json"));
        Check(new[] { "original-password", "updated-password", "fixture-master-password", saved.Host, "updated.invalid" }.All(s => !json.Contains(s)),
            "favorites and history contain no master password credential copies or endpoint snapshots");
        tree.SelectedNode = Nodes(tree.Nodes).Single(n => n.Tag is Session s && s.Id == saved.Id);
        form.DeleteSelectedSession(() => true);
        Check(store.GetPassword(saved.Id) == "" && !repo.Load().Favorites.Contains(saved.Id) && repo.Load().Recent.All(r => r.SessionId != saved.Id),
            "deleting a credential-bearing session removes its vault entry and both activity associations");
        foreach (TabPage page in FindControl<TabControl>(form).TabPages) page.Tag = null;
        form.Close(); PumpUntil(() => form.IsDisposed);
    }
}
