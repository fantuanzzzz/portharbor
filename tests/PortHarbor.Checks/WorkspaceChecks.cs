using System.Drawing;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void WorkspaceStorage(string root)
    {
        foreach (bool portable in new[] { false, true })
        {
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "workspace-" + portable), portable));
            store.PutSession(new Session { Host = "kept.invalid" }, "");
            byte[] before = File.ReadAllBytes(store.FilePath);
            string path = Path.Combine(store.DirectoryPath, "workspace-layout.json");
            var service = new WorkspaceLayoutService(new WorkspaceLayoutRepository(store.Paths)); service.Load();
            Check(service.Current == new WorkspaceLayout() && !service.LoadFailed && !File.Exists(path),
                $"{portable} missing workspace data uses defaults without creating a file");
            var expected = new WorkspaceLayout { X = -1500, Y = 100, Width = 1100, Height = 720, HasPosition = true,
                Maximized = true, SidebarWidth = 320, SidebarVisible = false, Dpi = 120, Theme = InterfaceTheme.Dark };
            service.Save(expected);
            var restarted = new WorkspaceLayoutService(new WorkspaceLayoutRepository(store.Paths)); restarted.Load();
            Check(restarted.Current == expected && before.SequenceEqual(File.ReadAllBytes(store.FilePath)),
                $"{portable} workspace layout survives restart independently of session storage");
            var bytes = File.ReadAllBytes(path);
            using (var occupied = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false; try { restarted.Save(new()); } catch (IOException) { failed = true; }
                Check(failed && restarted.Current == expected && bytes.SequenceEqual(File.ReadAllBytes(path)),
                    $"{portable} failed layout replacement preserves persisted and active values");
            }
            restarted.Save(new());
            Check(File.ReadAllBytes(path + ".bak").SequenceEqual(bytes), $"{portable} layout replacement keeps the previous version backup");
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"Layout\":{\"SidebarWidth\":290}}"); restarted.Load();
            Check(restarted.Current == new WorkspaceLayout { SidebarWidth = 290 }, $"{portable} missing layout fields retain defaults");
            foreach (string broken in new[] { "{broken", "{\"SchemaVersion\":2,\"Layout\":{}}", "{\"SchemaVersion\":1,\"Layout\":{\"Dpi\":0}}", "{\"SchemaVersion\":1,\"Layout\":{\"Theme\":99}}" })
            {
                File.WriteAllText(path, broken); restarted.Load();
                bool refused = false; try { restarted.Save(new()); } catch (Exception e) when (WorkspaceLayoutService.IsStorageFailure(e)) { refused = true; }
                Check(restarted.LoadFailed && restarted.Current == new WorkspaceLayout() && refused && File.ReadAllText(path) == broken,
                    $"{portable} bad future or invalid layouts fall back without overwriting the original");
            }
        }
    }

    static void WorkspaceUi(string root, string screenshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "workspace-ui"), true));
        var session = new Session { Host = "fixture.invalid", Name = "工作台测试", Protocol = Protocol.Telnet, Port = 23 }; store.PutSession(session, "");
        var repository = new WorkspaceLayoutRepository(store.Paths);
        MainForm Create(bool remember = true) => new(store, new ConfigurationService(store, store, store),
            (_, _, _) => new UnreportedTerminalFailure(), rememberLayout: remember);
        void Close(MainForm form)
        {
            foreach (TabPage page in FindControl<TabControl>(form).TabPages) page.Tag = null;
            form.Close(); PumpUntil(() => form.IsDisposed);
        }
        using (var form = Create())
        {
            form.Show(); Application.DoEvents();
            var split = FindControl<SplitContainer>(form); var tree = FindControl<TreeView>(form);
            var search = FindControl<TextBox>(form); var actions = form.Controls.OfType<ToolStrip>().Single(t => t.Name == "WorkspaceActions");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(Path.GetDirectoryName(screenshot)!, "workspace-default.png")); }
            Check(!actions.Items["ConnectSelected"]!.Enabled && !actions.Items["EditSelected"]!.Enabled,
                "workspace connect and edit actions are unavailable without a selected session");
            tree.SelectedNode = tree.Nodes.Cast<TreeNode>().SelectMany(n => n.Nodes.Cast<TreeNode>()).Single(n => n.Tag is Session);
            Check(actions.Items["ConnectSelected"]!.Enabled && actions.Items["EditSelected"]!.Enabled,
                "workspace actions enable when a saved session is selected");
            actions.Items["ConnectSelected"]!.PerformClick(); Application.DoEvents();
            Check(FindControl<TabControl>(form).SelectedTab!.Tag is Session s && s.Id == session.Id,
                "workspace connect action uses the existing saved-session connection path");
            int tabCount = FindControl<TabControl>(form).TabCount;
            tree.Focus(); SendTabMessage(tree.Handle, 0x100, new IntPtr((int)Keys.Enter), IntPtr.Zero); Application.DoEvents();
            Check(FindControl<TabControl>(form).TabCount == tabCount + 1, "Enter in the session tree connects the selected session");
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                bool editorOpened = false;
                timer.Tick += (_, _) => { timer.Stop(); var editor = Application.OpenForms.OfType<SessionEditor>().Single(); editorOpened = true; editor.Close(); };
                timer.Start(); tree.Focus(); SendTabMessage(tree.Handle, 0x100, new IntPtr((int)Keys.F2), IntPtr.Zero);
                Check(editorOpened, "F2 in the session tree opens the existing session editor");
            }
            form.HandleWorkspaceKey(Keys.Control | Keys.Shift | Keys.F);
            Check(search.Focused, "workspace search shortcut focuses the session search");
            form.HandleWorkspaceKey(Keys.F6); bool treeFocused = tree.Focused;
            form.HandleWorkspaceKey(Keys.F6); bool terminalFocused = FindControl<TerminalView>(FindControl<TabControl>(form).SelectedTab!).Focused;
            form.HandleWorkspaceKey(Keys.Shift | Keys.F6);
            Check(treeFocused && terminalFocused && tree.Focused, "F6 and Shift+F6 traverse search tree and terminal workspace");
            form.HandleWorkspaceKey(Keys.Control | Keys.Shift | Keys.B);
            Check(split.Panel1Collapsed, "workspace sidebar shortcut hides the session library");
            form.HandleWorkspaceKey(Keys.Control | Keys.Shift | Keys.F);
            Check(!split.Panel1Collapsed && search.Focused, "search restores a hidden sidebar before focusing the input");
            form.Size = form.MinimumSize; Application.DoEvents();
            var welcome = FindControl<TabControl>(form).TabPages[0]; FindControl<TabControl>(form).SelectedTab = welcome;
            Application.DoEvents();
            var choices = Descendants(welcome).OfType<FlowLayoutPanel>().Single(p => p.Name == "ProtocolChoices");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(screenshot); }
            Check(choices.Controls.OfType<Button>().Count() == 7 && choices.Controls.OfType<Button>().Select(b => b.Top).Distinct().Count() >= 2
                && choices.Controls.OfType<Button>().All(b => b.Right <= choices.ClientSize.Width && b.Bottom <= choices.ClientSize.Height)
                && choices.Width <= choices.Parent!.ClientSize.Width - choices.Parent.Padding.Horizontal,
                "all seven welcome actions wrap inside a narrow workspace");
            var area = Screen.FromControl(form).WorkingArea;
            form.Bounds = new Rectangle(area.Left + 20, area.Top + 20, Math.Min(1000, area.Width - 40), Math.Min(700, area.Height - 40));
            Application.DoEvents(); split.SplitterDistance = 300; form.HandleWorkspaceKey(Keys.Control | Keys.Shift | Keys.B);
            Check(form.SaveWorkspaceLayout(), "workspace layout changes save successfully");
            var saved = repository.Load();
            Check(saved.HasPosition && saved.Width == form.Width && !saved.SidebarVisible && saved.SidebarWidth == (int)Math.Round(300 * 96d / form.DeviceDpi),
                "workspace saves normal bounds and the hidden sidebar's preferred width");
            form.Location = new Point(form.Left + 1, form.Top);
            PumpUntil(() => repository.Load().X == form.Left);
            Check(repository.Load().X == form.Left, "workspace movement is persisted automatically after the debounce interval");
            form.WindowState = FormWindowState.Maximized; Application.DoEvents(); form.SaveWorkspaceLayout();
            Check(repository.Load().Maximized && repository.Load().Width == saved.Width,
                "maximizing retains the last normal window bounds");
            form.WindowState = FormWindowState.Minimized; Application.DoEvents(); form.SaveWorkspaceLayout();
            Check(repository.Load().Maximized && repository.Load().Width == saved.Width,
                "minimizing does not replace the normal bounds or maximized startup state");
            Close(form);
        }
        using (var restarted = Create())
        {
            restarted.Show(); Application.DoEvents();
            var split = FindControl<SplitContainer>(restarted);
            Check(restarted.WindowState == FormWindowState.Maximized && split.Panel1Collapsed,
                "restarting restores maximized state and a hidden sidebar");
            restarted.WindowState = FormWindowState.Normal; restarted.HandleWorkspaceKey(Keys.Control | Keys.Shift | Keys.B); Application.DoEvents();
            Check(Math.Abs(split.SplitterDistance - 300) <= 2, "showing the sidebar after restart restores its dragged width");
            Check(restarted.Width == repository.Load().Width && restarted.Height == repository.Load().Height,
                "restoring a maximized window recovers the saved normal size");
            string path = Path.Combine(store.DirectoryPath, "workspace-layout.json"); var bytes = File.ReadAllBytes(path);
            using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                restarted.Location = new Point(restarted.Left + 1, restarted.Top);
                bool saved = restarted.SaveWorkspaceLayout();
                Check(!saved && bytes.SequenceEqual(File.ReadAllBytes(path))
                    && restarted.Controls.OfType<StatusStrip>().Single().Items["LayoutMessage"]!.Text!.Contains("未保存"),
                    "a workspace write failure shows a nonmodal status and preserves the old file");
            }
            restarted.ResetWorkspaceLayout(); Application.DoEvents();
            Check(restarted.WindowState == FormWindowState.Normal && !split.Panel1Collapsed && repository.Load().SidebarVisible
                && !repository.Load().Maximized, "reset layout applies and saves normal defaults without changing sessions");
            Close(restarted);
        }
        using (var constrained = Create())
        {
            constrained.Show(); Application.DoEvents();
            var split = FindControl<SplitContainer>(constrained); constrained.Width = Math.Max(constrained.MinimumSize.Width + 400, 1450);
            Application.DoEvents(); split.SplitterDistance = constrained.Width / 2;
            int requested = split.SplitterDistance;
            constrained.Width = constrained.MinimumSize.Width; Application.DoEvents();
            constrained.Width = Math.Max(constrained.MinimumSize.Width + 400, 1450); Application.DoEvents();
            Check(split.SplitterDistance == requested, "temporary window narrowing does not forget the user's wider sidebar preference");
            Close(constrained);
        }
        var removedMonitor = repository.Load() with { X = 900000, Y = -900000, HasPosition = true };
        repository.Save(removedMonitor);
        using (var recovered = Create())
        {
            recovered.Show(); Application.DoEvents();
            Check(Screen.AllScreens.Any(s => s.WorkingArea.Contains(recovered.Bounds)), "saved offscreen coordinates restore within an existing monitor");
            Close(recovered);
        }
        string file = Path.Combine(store.DirectoryPath, "workspace-layout.json"); File.WriteAllText(file, "{broken");
        using (var fallback = Create())
        {
            fallback.Show(); Application.DoEvents(); fallback.ResetWorkspaceLayout();
            Check(File.ReadAllText(file) == "{broken" && !fallback.SaveWorkspaceLayout(),
                "default layout recovery cannot silently overwrite a corrupt saved layout");
            Close(fallback);
        }
        using (var smokeStore = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "workspace-smoke"), true)))
        using (var smoke = new MainForm(smokeStore, rememberLayout: false))
        {
            smoke.Show(); Application.DoEvents(); smoke.SaveWorkspaceLayout(); smoke.Close(); PumpUntil(() => smoke.IsDisposed);
            Check(!File.Exists(Path.Combine(smokeStore.DirectoryPath, "workspace-layout.json")), "startup smoke never writes workspace layout into a distributable package");
        }
    }
}
