using System.Drawing;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static T EditorControl<T>(Control editor, string name) where T : Control
        => Descendants(editor).OfType<T>().Single(c => c.Name == name);

    static void SessionEditorUi(string root, string output)
    {
        Directory.CreateDirectory(output);
        void Snapshot(Form editor, string name)
        {
            Application.DoEvents();
            using var bitmap = new Bitmap(editor.Width, editor.Height);
            editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size)); bitmap.Save(Path.Combine(output, name + ".png"));
        }
        var source = new Session { Name = "核心服务器", Host = "original.invalid", Port = 2222, Group = "工作/服务器", PrivateKey = "key-original", Arguments = "/original" };
        using (var editor = new SessionEditor(source, "one-time-secret", serialPorts: new CapabilitySerialProvider()))
        {
            editor.Show(); Application.DoEvents();
            Check(EditorControl<TextBox>(editor, "Host").Visible && !EditorControl<TextBox>(editor, "PrivateKey").Visible
                && !EditorControl<ComboBox>(editor, "SerialPort").Visible && !EditorControl<TextBox>(editor, "Executable").Visible,
                "editor presents only basic fields for the selected protocol and initially collapses advanced settings");
            Check(EditorControl<TextBox>(editor, "Password").UseSystemPasswordChar && !EditorControl<CheckBox>(editor, "ShowPassword").Checked,
                "editor starts with masked password and an explicit reveal control");
            var nameInput = EditorControl<TextBox>(editor, "Name"); nameInput.Focus();
            var tabKey = Message.Create(nameInput.Handle, 0x100, new IntPtr((int)Keys.Tab), IntPtr.Zero);
            nameInput.PreProcessMessage(ref tabKey); Application.DoEvents();
            Check(EditorControl<ComboBox>(editor, "Group").Focused, "Tab moves from session name to its next visible field");
            EditorControl<CheckBox>(editor, "ShowPassword").Checked = true;
            Check(!EditorControl<TextBox>(editor, "Password").UseSystemPasswordChar && editor.Password == "one-time-secret",
                "revealing a password changes presentation without changing its value");
            var choices = EditorControl<ComboBox>(editor, "Protocol"); choices.SelectedItem = "Serial";
            Check(!EditorControl<CheckBox>(editor, "ShowPassword").Checked && EditorControl<TextBox>(editor, "Password").UseSystemPasswordChar
                && EditorControl<ComboBox>(editor, "SerialPort").Visible && !EditorControl<TextBox>(editor, "Host").Visible,
                "switching to a non-password protocol masks the retained secret and shows serial fields");
            choices.SelectedItem = "FTP"; EditorControl<NumericUpDown>(editor, "Port").Value = 2121;
            choices.SelectedItem = "SSH";
            Check(EditorControl<NumericUpDown>(editor, "Port").Value == 2222 && editor.Password == "one-time-secret",
                "returning to SSH retains its custom port and unsaved password draft");
            choices.SelectedItem = "FTP";
            Check(EditorControl<NumericUpDown>(editor, "Port").Value == 2121,
                "each visited network protocol retains its own port draft");
            EditorControl<CheckBox>(editor, "ExpandAdvanced").Checked = true;
            EditorControl<CheckBox>(editor, "UseTls").Checked = true;
            EditorControl<TextBox>(editor, "RemotePath").Text = "/临时目录";
            choices.SelectedItem = "Shell";
            EditorControl<TextBox>(editor, "Arguments").Text = "  /keep spacing  ";
            choices.SelectedItem = "FTP";
            Check(EditorControl<CheckBox>(editor, "UseTls").Checked && EditorControl<TextBox>(editor, "RemotePath").Text == "/临时目录"
                && !EditorControl<TextBox>(editor, "PrivateKey").Visible,
                "protocol-specific advanced drafts survive switches while unrelated fields stay hidden");
            choices.SelectedItem = "SSH";
            EditorControl<TextBox>(editor, "Name").Text = ""; EditorControl<TextBox>(editor, "Host").Text = "bad host";
            EditorControl<ComboBox>(editor, "Group").Text = "工作//无效";
            EditorControl<Button>(editor, "SaveAndConnect").PerformClick(); Application.DoEvents();
            Check(editor.Visible && editor.DialogResult != DialogResult.OK && EditorControl<TextBox>(editor, "Name").Focused
                && EditorControl<Label>(editor, "ErrorName").Visible && EditorControl<Label>(editor, "ErrorHost").Visible
                && EditorControl<Label>(editor, "ErrorGroup").Visible && editor.Result.Name == source.Name,
                "invalid submission remains open highlights all invalid fields and focuses the first without publishing a result");
            Snapshot(editor, "editor-validation-light");
            EditorControl<TextBox>(editor, "Name").Text = "已修正";
            Check(!EditorControl<Label>(editor, "ErrorName").Visible && EditorControl<Label>(editor, "ErrorHost").Visible,
                "correcting one field clears its own message while retaining other invalid-field messages");
            EditorControl<TextBox>(editor, "Host").Text = "fixed.invalid"; EditorControl<ComboBox>(editor, "Group").Text = "工作/有效";
            Check(!EditorControl<Label>(editor, "ErrorHost").Visible && !EditorControl<Label>(editor, "ErrorGroup").Visible
                && EditorControl<Label>(editor, "EditorFeedback").Text == "",
                "correcting all fields clears inline errors and summary immediately");
            editor.Size = editor.MinimumSize; Application.DoEvents();
            var actions = EditorControl<FlowLayoutPanel>(editor, "EditorActions");
            Check(actions.Controls.OfType<Button>().All(b => b.Visible && editor.RectangleToScreen(editor.ClientRectangle).Contains(b.RectangleToScreen(b.ClientRectangle)))
                && EditorControl<Button>(editor, "SaveAndConnect").Width >= TextRenderer.MeasureText("保存并连接", editor.Font).Width,
                "all four editor actions remain visible and their labels fit at the minimum size");
            Snapshot(editor, "editor-minimum-light");
            var passwordSaving = EditorControl<CheckBox>(editor, "SavePassword");
            Check(passwordSaving.Width <= passwordSaving.Parent!.ClientSize.Width && passwordSaving.Height >= passwordSaving.Font.Height,
                "long password-saving explanation stays within its field column at the minimum width");
            editor.Size = new Size(720, 790); UiTheme.Apply(editor, InterfaceTheme.Dark); Application.DoEvents();
            Snapshot(editor, "editor-advanced-dark");
            EditorControl<Button>(editor, "CancelEditor").PerformClick();
            Check(editor.DialogResult == DialogResult.Cancel && source.Name == "核心服务器" && source.Host == "original.invalid"
                && source.PrivateKey == "key-original" && source.Arguments == "/original",
                "cancelling after extensive edits does not mutate the original session");
        }
        foreach (var key in new[] { Keys.Enter, Keys.Escape })
        {
            using var editor = new SessionEditor(source, serialPorts: new CapabilitySerialProvider());
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (_, _) =>
            {
                timer.Stop(); var name = EditorControl<TextBox>(editor, "Name"); name.Focus();
                var message = Message.Create(name.Handle, 0x100, new IntPtr((int)key), IntPtr.Zero);
                name.PreProcessMessage(ref message);
            };
            timer.Start(); editor.ShowDialog();
            Check(key == Keys.Enter ? editor.DialogResult == DialogResult.OK && editor.Persist && editor.ConnectNow : editor.DialogResult == DialogResult.Cancel && !editor.Persist && !editor.ConnectNow,
                key + " follows the editor default submit or cancel behavior from a text field");
        }
        foreach (var (action, persist, connect) in new[] { ("ConnectOnly", false, true), ("SaveOnly", true, false), ("SaveAndConnect", true, true) })
        {
            using var editor = new SessionEditor(source, "draft-secret", serialPorts: new CapabilitySerialProvider());
            using var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (_, _) => { timer.Stop(); EditorControl<TextBox>(editor, "Name").Text = action; EditorControl<Button>(editor, action).PerformClick(); };
            timer.Start(); editor.ShowDialog();
            Check(editor.DialogResult == DialogResult.OK && editor.Persist == persist && editor.ConnectNow == connect
                && editor.Result.Id == source.Id && editor.Result.Name == action && editor.Password == "draft-secret",
                action + " explicitly returns its persistence and connection intent without changing session identity");
        }
        using (var editor = new SessionEditor(new Session { Protocol = Protocol.RDP, Host = "desktop.invalid:3390" }, serialPorts: new CapabilitySerialProvider()))
        {
            editor.Show(); Application.DoEvents(); EditorControl<Button>(editor, "SaveOnly").PerformClick();
            Check(EditorControl<Label>(editor, "ErrorHost").Text.Contains("单独的端口") && EditorControl<TextBox>(editor, "Host").Focused,
                "RDP host-with-port validation identifies and focuses the host input");
            EditorControl<ComboBox>(editor, "Protocol").SelectedItem = "Shell";
            Check(!EditorControl<Label>(editor, "ErrorHost").Visible && EditorControl<Label>(editor, "EditorFeedback").Text == "",
                "switching protocols removes errors for fields that no longer apply");
            EditorControl<TextBox>(editor, "Executable").Text = ""; EditorControl<Button>(editor, "SaveOnly").PerformClick();
            Check(EditorControl<Label>(editor, "ErrorExecutable").Visible && EditorControl<TextBox>(editor, "Executable").Focused,
                "missing shell program is reported inline at its path field");
            editor.Close();
        }
        var capabilities = new FixturePlatformCapabilities();
        capabilities.States[PlatformCapability.LocalShell] = new(false, PlatformUnavailableReason.MissingSystemComponent);
        using (var editor = new SessionEditor(new Session { Protocol = Protocol.Shell }, capabilities: capabilities, serialPorts: new CapabilitySerialProvider()))
        {
            editor.Show(); Application.DoEvents();
            Check(!EditorControl<Button>(editor, "ConnectOnly").Enabled && !EditorControl<Button>(editor, "SaveAndConnect").Enabled
                && EditorControl<Button>(editor, "SaveOnly").Enabled, "unavailable platform capability disables both connection actions and permits saving");
            capabilities.States[PlatformCapability.LocalShell] = PlatformCapabilityStatus.Available;
            EditorControl<ComboBox>(editor, "Protocol").SelectedItem = "SSH"; EditorControl<ComboBox>(editor, "Protocol").SelectedItem = "Shell";
            capabilities.States[PlatformCapability.LocalShell] = new(false, PlatformUnavailableReason.InspectionFailed);
            EditorControl<Button>(editor, "ConnectOnly").PerformClick();
            Check(editor.Visible && editor.DialogResult != DialogResult.OK && !EditorControl<Button>(editor, "ConnectOnly").Enabled,
                "connect-only action rechecks capability before accepting a previously enabled submission");
            EditorControl<Button>(editor, "SaveOnly").PerformClick();
            Check(editor.Persist && !editor.ConnectNow && editor.DialogResult == DialogResult.OK,
                "save-only still completes after a capability disappears");
        }
        SessionEditorPickerUi(output);
        SessionEditorActionsUi(root);
    }

    sealed class EditorPathPicker : IFilePicker, IFolderPicker
    {
        internal string? Path;
        internal bool Fail;
        internal readonly List<FilePickerOptions> Files = [];
        internal readonly List<string> Folders = [];
        public string? OpenFile(FilePickerOptions options) { Files.Add(options); if (Fail) throw new IOException("private fixture details"); return Path; }
        public string? SaveFile(FilePickerOptions options) => throw new Exception("unexpected save picker");
        public string? SelectFolder(string selectedPath = "") { Folders.Add(selectedPath); if (Fail) throw new IOException("private fixture details"); return Path; }
    }

    static void SessionEditorPickerUi(string output)
    {
        var picker = new EditorPathPicker();
        using var editor = new SessionEditor(new Session { Host = "fixture.invalid", PrivateKey = "original.key" }, "secret",
            serialPorts: new CapabilitySerialProvider(), filePicker: picker, folderPicker: picker);
        editor.Show(); Application.DoEvents(); EditorControl<CheckBox>(editor, "ExpandAdvanced").Checked = true;
        EditorControl<Button>(editor, "BrowsePrivateKey").PerformClick();
        Check(EditorControl<TextBox>(editor, "PrivateKey").Text == "original.key" && picker.Files.Single().FileName == "original.key",
            "cancelled private-key selection preserves its draft and forwards the current path");
        string chosenKey = System.IO.Path.Combine(output, "私钥 空格 🔑.key"); picker.Path = chosenKey;
        EditorControl<Button>(editor, "BrowsePrivateKey").PerformClick();
        Check(EditorControl<TextBox>(editor, "PrivateKey").Text == chosenKey && editor.Password == "secret" && !File.Exists(chosenKey),
            "chosen private-key path preserves Unicode and changes neither the secret nor files");
        EditorControl<ComboBox>(editor, "Protocol").SelectedItem = "Shell";
        picker.Path = System.IO.Path.Combine(output, "Shell 程序.exe"); EditorControl<Button>(editor, "BrowseExecutable").PerformClick();
        Check(EditorControl<TextBox>(editor, "Executable").Text == picker.Path && picker.Files.Last().Filter.Contains("*.exe;*.com"),
            "shell program picker uses an executable filter and retains the exact selected path");
        picker.Path = System.IO.Path.Combine(output, "工作 目录"); EditorControl<Button>(editor, "BrowseWorkingDirectory").PerformClick();
        string directory = EditorControl<TextBox>(editor, "WorkingDirectory").Text;
        picker.Path = null; EditorControl<Button>(editor, "BrowseWorkingDirectory").PerformClick();
        Check(EditorControl<TextBox>(editor, "WorkingDirectory").Text == directory && picker.Folders.Last() == directory && !Directory.Exists(directory),
            "directory selection cancellation retains its draft without creating a directory");
        picker.Fail = true; EditorControl<Button>(editor, "BrowseWorkingDirectory").PerformClick();
        Check(editor.Visible && EditorControl<Label>(editor, "EditorFeedback").Text.Contains("直接输入")
            && !EditorControl<Label>(editor, "EditorFeedback").Text.Contains("private fixture"),
            "path picker failure leaves the editor usable with a safe message and manual input fallback");
        EditorControl<TextBox>(editor, "WorkingDirectory").Text = "";
        EditorControl<TextBox>(editor, "Executable").Text = "cmd.exe";
        EditorControl<TextBox>(editor, "Arguments").Text = "  /Q  ";
        EditorControl<Button>(editor, "SaveOnly").PerformClick();
        Check(editor.Result.Executable == "cmd.exe" && editor.Result.WorkingDirectory == "" && editor.Result.Arguments == "  /Q  ",
            "manual shell commands optional working directory and exact argument spacing remain supported");
    }

    static void SessionEditorActionsUi(string root)
    {
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "editor-actions"), true));
        var saved = new Session { Name = "原会话", Protocol = Protocol.Telnet, Host = "original.invalid", Port = 23 };
        store.PutSession(saved, "");
        var opened = new List<(Session Session, string Secret)>();
        using var form = new MainForm(store, new ConfigurationService(store, store, store),
            (session, secret, _) => { opened.Add((session, secret)); return new UnreportedTerminalFailure(); });
        form.Show(); Application.DoEvents();
        var sessions = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "会话");
        var edit = sessions.DropDownItems.Cast<ToolStripItem>().Single(i => i.Text == "编辑…");
        var tree = FindControl<TreeView>(form);
        void Select()
        {
            IEnumerable<TreeNode> Walk(TreeNodeCollection nodes)
            { foreach (TreeNode node in nodes) { yield return node; foreach (var child in Walk(node.Nodes)) yield return child; } }
            tree.SelectedNode = Walk(tree.Nodes).Single(n => n.Tag is Session s && s.Id == saved.Id);
        }
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
            timer.Start(); open(); if (failure != null) throw failure; Application.DoEvents();
        }
        Select(); byte[] original = File.ReadAllBytes(store.FilePath);
        Interact(edit.PerformClick, dialog =>
        {
            EditorControl<TextBox>(dialog, "Host").Text = "temporary.invalid";
            EditorControl<Button>(dialog, "ConnectOnly").PerformClick();
        });
        Check(opened.Single().Session.Host == "temporary.invalid" && original.SequenceEqual(File.ReadAllBytes(store.FilePath))
            && store.GetSessions().Single().Host == "original.invalid", "connect-only uses edited parameters while leaving the persisted session byte-for-byte unchanged");
        int starts = opened.Count; Select();
        Interact(edit.PerformClick, dialog =>
        {
            EditorControl<TextBox>(dialog, "Name").Text = "仅保存结果";
            EditorControl<Button>(dialog, "SaveOnly").PerformClick();
        });
        Check(store.GetSessions().Single().Name == "仅保存结果" && opened.Count == starts,
            "save-only updates the session without starting a transport");
        Select();
        Interact(edit.PerformClick, dialog =>
        {
            EditorControl<TextBox>(dialog, "Name").Text = "保存并连接结果";
            EditorControl<TextBox>(dialog, "Host").Text = "saved-new.invalid";
            EditorControl<Button>(dialog, "SaveAndConnect").PerformClick();
        });
        Check(opened.Count == starts + 1 && opened.Last().Session.Host == "saved-new.invalid" && store.GetSessions().Single().Host == "saved-new.invalid",
            "save-and-connect persists the edited values and starts exactly one connection with those values");
        store.SetMasterPassword("fixture-master-password");
        saved = saved with { Protocol = Protocol.SSH, Host = "secret.invalid", SavePassword = true };
        store.PutSession(saved, "existing-secret");
        form.SetLibraryView(SessionLibraryView.All); Select(); store.LockVault();
        byte[] locked = File.ReadAllBytes(store.FilePath); starts = opened.Count;
        Interact(edit.PerformClick, dialog =>
        {
            Check(dialog is not SessionEditor && dialog.Text == "解锁密码库", "editing a locked saved credential requests unlock before exposing an empty password draft");
            dialog.DialogResult = DialogResult.Cancel;
        });
        store.UnlockVault("fixture-master-password");
        Check(locked.SequenceEqual(File.ReadAllBytes(store.FilePath)) && store.GetPassword(saved.Id) == "existing-secret" && opened.Count == starts,
            "cancelling editor vault unlock preserves the original encrypted credential and starts no connection");
        Interact(edit.PerformClick, dialog =>
        {
            Check(((SessionEditor)dialog).Password == "existing-secret", "unlocked editor loads the current saved credential");
            EditorControl<TextBox>(dialog, "Name").Text = "保留密码"; EditorControl<Button>(dialog, "SaveOnly").PerformClick();
        });
        Check(store.GetPassword(saved.Id) == "existing-secret" && store.GetSessions().Single().Name == "保留密码" && opened.Count == starts,
            "saving an unrelated field preserves the original stored credential");
        foreach (TabPage page in FindControl<TabControl>(form).TabPages) page.Tag = null;
        form.Close(); PumpUntil(() => form.IsDisposed);
    }
}
