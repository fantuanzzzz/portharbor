using System.Text.Json;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void FilePickerAdapters(string root)
    {
        var assembly = typeof(WindowsFilePicker).Assembly;
        Check(assembly != typeof(MainForm).Assembly && assembly != typeof(WindowsSecretProtector).Assembly
            && assembly.GetReferencedAssemblies().All(a => a.Name is not ("PortHarbor.Infrastructure" or "PortHarbor"))
            && typeof(IFilePicker).Assembly == typeof(SessionService).Assembly
            && typeof(IFilePicker).Assembly.GetReferencedAssemblies().All(a => a.Name != "System.Windows.Forms"),
            "file selection lives in a dedicated Windows Forms adapter while the application port stays UI independent");
        var owner = new FixtureDialogOwner();
        var dialogs = new List<FileDialog>();
        int disposed = 0;
        string selected = Path.Combine(root, "选择 空格 🔑.json");
        var picker = new WindowsFilePicker(owner, (dialog, window) =>
        {
            dialogs.Add(dialog);
            dialog.Disposed += (_, _) => disposed++;
            Check(ReferenceEquals(owner, window) && dialog.Filter == "JSON (*.json)|*.json" && dialog.FileName == "default.json",
                "Windows " + (dialog is OpenFileDialog ? "open" : "save") + " picker forwards its modal owner, filter and proposed file name");
            if (dialog is OpenFileDialog open)
                Check(!open.Multiselect && open.CheckFileExists && open.CheckPathExists,
                    "Windows open picker keeps single existing-file selection");
            if (dialog is SaveFileDialog save)
                Check(save.OverwritePrompt && save.CheckPathExists && !save.CheckFileExists,
                    "Windows save picker keeps overwrite confirmation and allows a new file");
            dialog.FileName = selected;
            return DialogResult.OK;
        });
        var options = new FilePickerOptions("JSON (*.json)|*.json", "default.json");
        Check(picker.OpenFile(options) == selected && picker.SaveFile(options) == selected
            && !File.Exists(selected) && disposed == 2 && !ReferenceEquals(dialogs[0], dialogs[1]),
            "successful file selections preserve Unicode paths without writing files and dispose each fresh dialog");
        picker = new WindowsFilePicker(owner, (dialog, _) =>
        {
            dialog.Disposed += (_, _) => disposed++; dialog.FileName = selected; return DialogResult.Cancel;
        });
        Check(picker.OpenFile(options) == null && picker.SaveFile(options) == null && disposed == 4,
            "cancelled open and save discard the proposed path and dispose their dialogs");
        picker = new WindowsFilePicker(owner, (dialog, _) =>
        {
            dialog.Disposed += (_, _) => disposed++; throw new InvalidOperationException("fixture dialog failure");
        });
        Check(Throws<InvalidOperationException>(() => picker.OpenFile(options))
            && Throws<InvalidOperationException>(() => picker.SaveFile(options)) && disposed == 6,
            "dialog startup failures propagate and release both open and save dialogs");
        Check(Throws<ArgumentNullException>(() => picker.OpenFile(null!))
            && Throws<ArgumentNullException>(() => picker.SaveFile(null!)) && disposed == 6,
            "invalid picker requests are rejected before creating native dialogs");
    }

    static void FilePickerUi(string root)
    {
        string directory = Path.Combine(root, "file-picker-ui"); Directory.CreateDirectory(directory);
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(directory, "config"), true));
        store.SetMasterPassword("fixture-master");
        var session = new Session { Name = "Picker export", Host = "picker.invalid", SavePassword = true };
        store.PutSession(session, "fixture-secret");
        store.CheckHostKey(session.Host, session.Port, "SHA256:fixture-trust", _ => true);
        var picker = new FixtureFilePicker();
        using var form = new MainForm(store, new ConfigurationService(store, store, store), filePicker: picker);
        // The vault is already unlocked; showing this fixture does not prompt for credentials.
        form.Show(); Application.DoEvents();
        byte[] before = File.ReadAllBytes(store.FilePath); int confirmations = 0;
        form.Import(() => { confirmations++; return true; });
        Check(confirmations == 0 && before.SequenceEqual(File.ReadAllBytes(store.FilePath))
            && picker.OpenOptions.Last() == new FilePickerOptions("PortHarbor 会话 (*.json)|*.json"),
            "cancelled session selection bypasses confirmation and leaves stored configuration unchanged");
        picker.OpenPath = Path.Combine(directory, "unreadable-not-created.json");
        form.Import(() => { confirmations++; return false; });
        Check(confirmations == 1 && before.SequenceEqual(File.ReadAllBytes(store.FilePath)),
            "declined session import never opens the selected file or changes storage");
        string incoming = Path.Combine(directory, "导入 会话.json");
        var imported = new Session { Host = "imported-picker.invalid", SavePassword = true };
        File.WriteAllText(incoming, JsonSerializer.Serialize(new AppData
        {
            SchemaVersion = 1, Sessions = [imported], Credentials = new() { [imported.Id.ToString()] = "untrusted-credential" },
            HostKeys = new() { ["[imported-picker.invalid]:22"] = "untrusted-fingerprint" }
        }, Store.Json));
        picker.OpenPath = incoming; form.Import(() => { confirmations++; return true; });
        Check(confirmations == 2 && store.GetSessions().Count == 2 && store.Data.HostKeys.Count == 1
            && !store.GetSessions().Single(s => s.Host == imported.Host).SavePassword && store.Data.Credentials.Count == 1
            && FindControl<TreeView>(form).Nodes.Count > 0,
            "confirmed import reads the picked Unicode path and keeps untrusted credentials and host keys out of storage");
        var fileMenu = (ToolStripMenuItem)form.MainMenuStrip!.Items[0];
        var export = (ToolStripMenuItem)fileMenu.DropDownItems[1];
        string[] filesBefore = Directory.GetFiles(directory);
        export.PerformClick();
        Check(filesBefore.SequenceEqual(Directory.GetFiles(directory))
            && picker.SaveOptions.Last() == new FilePickerOptions("PortHarbor 会话 (*.json)|*.json", "portharbor-sessions.json"),
            "cancelled session export keeps the default JSON name and writes no file");
        string exported = Path.Combine(directory, "导出 会话.json"); picker.SavePath = exported; export.PerformClick();
        var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(exported), Store.Json)!;
        Check(data.Sessions.Count == 2 && data.Sessions.All(s => !s.SavePassword)
            && data.Credentials.Count == 0 && data.HostKeys.Count == 0 && data.Vault == null
            && !File.ReadAllText(exported).Contains("fixture-secret"),
            "picked session export contains sessions but no passwords, vault or host trust");
        form.Close(); Application.DoEvents();

        var backend = new MemoryRemoteFileSystem(); backend.Files["/bad:name?.txt"] = [1, 2, 3];
        picker = new FixtureFilePicker();
        using var transferForm = new Form { Width = 960, Height = 640 };
        var view = new FileView(new FileConnection(Protocol.SFTP, backend), "/", picker) { Dock = DockStyle.Fill };
        transferForm.Controls.Add(view); transferForm.Show();
        var listing = FindControl<ListView>(view);
        PumpPickerUiUntil(() => listing.Items.Count == 1 && view.Controls.OfType<Label>().Single().Text == "就绪");
        listing.Items[0].Selected = true; listing.Select(); Application.DoEvents();
        int reads = backend.OpenReads, writes = backend.OpenWrites, lists = backend.ListCalls;
        CompletePickerUi(view.Upload()); CompletePickerUi(view.Download());
        Check(backend.OpenReads == reads && backend.OpenWrites == writes && backend.ListCalls == lists
            && picker.OpenOptions.Single() == new FilePickerOptions()
            && picker.SaveOptions.Single() == new FilePickerOptions(FileName: "bad_name_.txt"),
            "cancelled upload and download never start transfers and download keeps the sanitized suggested file name");
        string local = Path.Combine(directory, "上传 文件.bin"); File.WriteAllBytes(local, [4, 5, 6, 7]);
        picker.OpenPath = local; CompletePickerUi(view.Upload());
        Check(backend.Files["/上传 文件.bin"].SequenceEqual(File.ReadAllBytes(local)) && backend.CommitCalls == 1,
            "upload uses the chosen local path and only its file name for the remote destination");
        var download = listing.Items.Cast<ListViewItem>().Single(i => i.Text == "bad:name?.txt");
        download.Selected = true; listing.Select(); Application.DoEvents();
        string destination = Path.Combine(directory, "另选 下载.bin"); picker.SavePath = destination;
        CompletePickerUi(view.Download());
        Check(File.ReadAllBytes(destination).SequenceEqual(new byte[] { 1, 2, 3 }) && backend.OpenReads == reads + 1
            && picker.SaveOptions.Last().FileName == "bad_name_.txt",
            "download uses the exact chosen local destination with the original remote source");
        CompletePickerUi(view.CloseAsync()); transferForm.Close(); Application.DoEvents();
        Check(backend.DisposeCalls == 1, "file selection integration retains asynchronous transfer ownership and cleanup");
    }

    static void CompletePickerUi(Task task)
    {
        PumpPickerUiUntil(() => task.IsCompleted); task.GetAwaiter().GetResult();
    }

    static void PumpPickerUiUntil(Func<bool> ready)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!ready() && deadline.Elapsed < TimeSpan.FromSeconds(8)) { Application.DoEvents(); Thread.Sleep(10); }
        if (!ready()) throw new TimeoutException("File picker UI fixture did not complete.");
    }

    sealed class FixtureFilePicker : IFilePicker
    {
        internal string? OpenPath, SavePath;
        internal readonly List<FilePickerOptions> OpenOptions = [], SaveOptions = [];
        public string? OpenFile(FilePickerOptions options) { OpenOptions.Add(options); return OpenPath; }
        public string? SaveFile(FilePickerOptions options) { SaveOptions.Add(options); return SavePath; }
    }

    sealed class FixtureDialogOwner : IWin32Window
    {
        public IntPtr Handle => new(1234);
    }
}
