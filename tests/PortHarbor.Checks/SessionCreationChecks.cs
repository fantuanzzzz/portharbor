using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void SessionCreationUi(string root, string output)
    {
        Directory.CreateDirectory(output);
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "session-creation"), true));
        int starts = 0;
        using var form = new MainForm(store, new ConfigurationService(store, store, store),
            (_, _, _) => { starts++; return new UnreportedTerminalFailure(); });
        form.Show(); Application.DoEvents();
        var tree = FindControl<TreeView>(form);
        TreeNode Root() => tree.Nodes["UserSessionsRoot"]!;
        IEnumerable<TreeNode> Walk(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes) { yield return node; foreach (var child in Walk(node.Nodes)) yield return child; }
        }
        TreeNode Group(string path) => Walk(tree.Nodes).Single(node => node.Tag as string == path);
        var menu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == "会话");
        var createGroup = menu.DropDownItems.Cast<ToolStripItem>().Single(item => item.Text == "新建分类…");
        void Interact(Action open, Action<Form> act)
        {
            Exception? failure = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (_, _) =>
            {
                timer.Stop(); var dialog = Application.OpenForms.Cast<Form>().Single(window => window.Modal);
                try { act(dialog); }
                catch (Exception error) { failure = error; }
                finally { if (!dialog.IsDisposed) dialog.Close(); }
            };
            timer.Start(); open(); if (failure != null) throw failure; Application.DoEvents();
        }
        void CreateCategory(string name)
        {
            Interact(createGroup.PerformClick, dialog =>
            {
                FindControl<TextBox>(dialog).Text = name;
                Descendants(dialog).OfType<Button>().Single(button => button.DialogResult == DialogResult.OK).PerformClick();
            });
        }

        Check(tree.Nodes.Count == 1 && Root().Text == "User sessions" && Root().Tag == null
            && Root().Nodes.Cast<TreeNode>().All(node => node.Tag as string == "默认")
            && store.Data.Groups.Count == 0, "empty libraries display a virtual User sessions root while preserving the existing default folder");
        Check(menu.DropDownItems.Cast<ToolStripItem>().Count(item => item.Text?.StartsWith("新建分类") == true) == 1
            && !menu.DropDownItems.Cast<ToolStripItem>().Any(item => item.Text?.Contains("根分类") == true || item.Text?.Contains("子分类") == true)
            && tree.ContextMenuStrip!.Items.Cast<ToolStripItem>().Count(item => item.Text == "新建分类") == 1,
            "session menu and context menu expose one unified category creation action");
        tree.SelectedNode = Root(); CreateCategory("工作");
        Check(store.Data.Groups.SequenceEqual(["工作"]) && tree.SelectedNode?.Tag as string == "工作" && Group("工作").Parent == Root(),
            "creating on User sessions adds and selects a top-level category");
        CreateCategory("交换机");
        Check(store.GetGroups().Contains("工作/交换机") && Group("工作/交换机").Parent == Group("工作"),
            "the same category action creates a child beneath the selected category");
        var saved = new Session { Name = "设备", Group = "工作/交换机", Protocol = Protocol.Telnet, Host = "switch.invalid", Port = 23 };
        store.PutSession(saved, ""); form.SetLibraryView(SessionLibraryView.All);
        tree.SelectedNode = Walk(tree.Nodes).Single(node => node.Tag is Session session && session.Id == saved.Id);
        CreateCategory("测试");
        Check(store.GetGroups().Contains("工作/交换机/测试"), "creating on a session uses that session's containing category");
        tree.SelectedNode = Root(); CreateCategory("实验室");
        Check(store.GetGroups().Contains("实验室") && !store.GetGroups().Contains("工作/交换机/测试/实验室"),
            "selecting User sessions makes top-level creation reachable after navigating nested categories");
        tree.SelectedNode = Root();
        var actions = form.Controls.OfType<ToolStrip>().Single(strip => strip.Name == "WorkspaceActions");
        Check(!actions.Items["ConnectSelected"]!.Enabled && !actions.Items["EditSelected"]!.Enabled,
            "the virtual library root cannot be connected or edited as a session");
        Interact(createGroup.PerformClick, dialog => Descendants(dialog).OfType<Button>().Single(button => button.DialogResult == DialogResult.Cancel).PerformClick());
        Check(store.Data.Groups.Count == 4, "cancelling unified category creation leaves the saved hierarchy unchanged");

        var newSession = menu.DropDownItems.Cast<ToolStripItem>().Single(item => item.Text == "新建连接…");
        tree.SelectedNode = Group("实验室");
        Interact(newSession.PerformClick, dialog =>
        {
            Check(dialog.Text == "新建连接" && EditorControl<TextBox>(dialog, "Name").Text == ""
                && EditorControl<ComboBox>(dialog, "Group").Text == "实验室", "new session starts unnamed in the selected category");
            EditorControl<TextBox>(dialog, "Host").Text = "192.0.2.42";
            EditorControl<Button>(dialog, "SaveOnly").PerformClick();
            Check(dialog.Visible && EditorControl<Label>(dialog, "ErrorUsername").Visible && EditorControl<TextBox>(dialog, "Username").Focused
                && !EditorControl<Label>(dialog, "ErrorName").Visible, "SSH save highlights its required username while the name stays optional");
            EditorControl<TextBox>(dialog, "Username").Text = "operator";
            EditorControl<Button>(dialog, "SaveOnly").PerformClick();
        });
        var automatic = store.GetSessions().Single(session => session.Host == "192.0.2.42");
        Check(automatic.Name == automatic.Host && automatic.Group == "实验室" && starts == 0,
            "saving an unnamed SSH session uses its host and category without starting a connection");
        FindControl<TextBox>(form).Text = "no-such-session-query"; Application.DoEvents();
        Check(tree.Nodes.Count == 1 && Root().Nodes.Count == 0, "no-result filtering preserves the virtual root without fake sessions");
        FindControl<TextBox>(form).Clear(); form.SetLibraryView(SessionLibraryView.Recent);
        Check(tree.Nodes.Count == 1 && Root().Nodes.Count == 0, "an empty recent view retains User sessions and no synthetic recent entries");
        form.SetLibraryView(SessionLibraryView.All);
        using (var bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(output, "user-sessions-root.png")); }

        foreach (var protocol in new[] { Protocol.SSH, Protocol.SFTP })
            foreach (var action in new[] { "SaveOnly", "ConnectOnly", "SaveAndConnect" })
            {
                using var editor = new SessionEditor(new Session { Name = "", Host = "auth.invalid", Protocol = protocol }, serialPorts: new CapabilitySerialProvider(), creating: true);
                editor.Show(form); Application.DoEvents();
                Check(EditorControl<TextBox>(editor, "Username").AccessibleName!.Contains("必填"), protocol + " username is visibly and accessibly required");
                EditorControl<TextBox>(editor, "Username").Text = "  "; EditorControl<Button>(editor, action).PerformClick();
                Check(editor.Visible && editor.DialogResult != DialogResult.OK && EditorControl<Label>(editor, "ErrorUsername").Visible,
                    protocol + " " + action + " rejects a blank username before accepting the draft");
                EditorControl<ComboBox>(editor, "Protocol").SelectedItem = "FTP";
                Check(!EditorControl<Label>(editor, "ErrorUsername").Visible && EditorControl<TextBox>(editor, "Username").AccessibleName!.Contains("可选"),
                    "switching to FTP clears the SSH-only username requirement");
                EditorControl<ComboBox>(editor, "Protocol").SelectedItem = protocol.ToString();
                EditorControl<TextBox>(editor, "Username").Text = "operator";
                EditorControl<TextBox>(editor, "Host").Text = "2001:db8::42";
                EditorControl<Button>(editor, action).PerformClick();
                Check(editor.DialogResult == DialogResult.OK && editor.Result.Name == "2001:db8::42" && editor.Result.Username == "operator"
                    && editor.Persist == (action != "ConnectOnly") && editor.ConnectNow == (action != "SaveOnly"),
                    protocol + " " + action + " resolves the latest host name and preserves submission intent");
            }
        using (var editor = new SessionEditor(serialPorts: new CapabilitySerialProvider()))
        {
            editor.Show(form); Application.DoEvents(); EditorControl<TextBox>(editor, "Username").Text = "operator";
            EditorControl<Button>(editor, "SaveOnly").PerformClick();
            Check(EditorControl<TextBox>(editor, "Host").Focused && !EditorControl<Label>(editor, "ErrorName").Visible,
                "an empty new draft requests its host rather than an optional display name");
            editor.Close();
        }
        var legacy = new Session { Host = "legacy.invalid", Username = "" }; store.PutSession(legacy, "");
        using (var restarted = new Store(new WindowsSecretProtector(), TestPaths(store.DirectoryPath, true)))
            Check(restarted.GetSessions().Any(session => session.Id == legacy.Id), "legacy SSH sessions without usernames remain readable after restart");
        bool rejected = false;
        try { form.Connect(legacy, ""); } catch (ArgumentException) { rejected = true; }
        Check(rejected && starts == 0, "legacy SSH connects require a username before creating a transport");
        form.Close(); Application.DoEvents();
    }
}
