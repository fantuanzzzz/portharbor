using System.Net;
using System.Net.Sockets;
using System.Text;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    static async Task CheckThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + " to be thrown.");
    }

    static int passed;
    static readonly List<string> results = [];
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; results.Add(name); Console.WriteLine("PASS: " + name); }
    [STAThread]
    static async Task<int> Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "PortHarbor-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string? failure = null;
        try
        {
            if (args.Length > 0 && args[0] == "--ui") { await Ui(root, args[1]); return 0; }
            if (args.Length > 0 && args[0] == "--ui-shell") { UiShell.Run(root, args[1]); return 0; }
            string? configPath = Option(args, "--integration");
            if (args.Contains("--unit"))
            {
                Storage(root); VaultAndGroups(root); Terminal(); Telnet(); await FileDiagnostics(root); await FileTransferContract(root);
            }
            else if (args.Contains("--windows"))
            {
                await Shell(root); await Lifecycle(root);
            }
            else if (configPath != null)
            {
                await TelnetRoundTrip(root); await Integration(root, configPath); await SshRoundTrip(root, configPath);
            }
            else
            {
                Storage(root); Terminal(); Telnet(); await TelnetRoundTrip(root);
                if (!args.Contains("--skip-shell")) await Shell(root);
                await FileTransferContract(root); await Lifecycle(root);
            }
            Console.WriteLine($"{passed} checks passed");
            return 0;
        }
        catch (Exception e) { failure = e.ToString(); Console.Error.WriteLine(e); return 1; }
        finally
        {
            string? resultPath = Option(args, "--results") ?? (args.Length == 1 && args[0].EndsWith(".json") ? args[0] : null);
            if (resultPath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
                File.WriteAllText(resultPath, System.Text.Json.JsonSerializer.Serialize(new { passed, results, failure, date = DateTimeOffset.Now }, Store.Json));
            }
        }
    }
    static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    // Configure check windows directly: both friend assemblies generate a global ApplicationConfiguration type.
    internal static void InitializeUi()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
    }

    static async Task Ui(string root, string output)
    {
        InitializeUi();
        using var uiStore = new Store(Path.Combine(root,"ui"));
        uiStore.AddGroup("空分类");
        uiStore.AddGroup("Work/Switches/Core");
        uiStore.PutSession(new Session { Name = "Nested terminal", Host = "nested-test-host", Group = "Work/Switches/Core" }, "");
        using var form = new MainForm(uiStore);
        form.Show(); Application.DoEvents();
        using var bitmap = new System.Drawing.Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,form.ClientRectangle); bitmap.Save(output);
        Check(form.Visible && form.Controls.Count>0,"main window renders without error");
        foreach(var p in Enum.GetValues<Protocol>()) { using var editor = new SessionEditor(new Session { Protocol=p,Port=Session.DefaultPort(p) }); editor.Show(); Application.DoEvents(); editor.Close(); }
        Check(true,"all seven protocol editors open without error");
        var tree = FindControl<TreeView>(form);
        Check(tree.Nodes.Cast<TreeNode>().Any(n => n.Text == "空分类" && n.Nodes.Count == 0), "sidebar renders saved empty category");
        var tabs = FindControl<TabControl>(form);
        var first = new TabPage("first"); var second = new TabPage("second");
        tabs.TabPages.Add(first); tabs.TabPages.Add(second); tabs.SelectedTab = second;
        await form.CloseTab(first, () => false);
        Check(tabs.TabPages.Contains(first) && !first.IsDisposed, "declined tab close preserves target page");
        await form.CloseTab(first, () => true);
        Check(first.IsDisposed && tabs.TabPages.Contains(second) && tabs.SelectedTab == second, "confirmed tab close disposes requested page and preserves other tab");
        form.Close();
        TabMouseUi();
        FileTransferUi();
        FileTransferFormCloseUi();
    }
    static T FindControl<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T found) return found;
            try { return FindControl<T>(child); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Control not found: " + typeof(T).Name);
    }
    static async Task SshRoundTrip(string root,string configPath)
    {
        using var config=System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        var s=new Session { Protocol=Protocol.SSH,Host="127.0.0.1",Port=config.RootElement.GetProperty("sftpPort").GetInt32(),Username="easytest" };
        using var connection=new TerminalConnection(s,new Store(Path.Combine(root,"ssh-terminal")),"easytest-only",_=>true);
        var text=new StringBuilder(); var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var echoed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Output+=value=> { lock(text) { text.Append(value); if(text.ToString().Contains("SSH_TEST_READY")) ready.TrySetResult(true); if(text.ToString().Contains("ECHO:ordered-input")) echoed.TrySetResult(true); } };
        Task run=connection.Run(80,24); await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); connection.Send(Encoding.UTF8.GetBytes("ordered-input")); await echoed.Task.WaitAsync(TimeSpan.FromSeconds(10)); connection.Resize(100,30); connection.Dispose(); await run.WaitAsync(TimeSpan.FromSeconds(10));
        Check(true,"SSH live authentication, host key, PTY, ordered input and disconnect");
    }
    static void Storage(string root)
    {
        var store = new Store(Path.Combine(root, "store")); var s = new Session { Name = "测试 Linux", Host = "127.0.0.1", SavePassword = true };
        const string secret = "secret-中文-!@#$-123";
        store.SetMasterPassword("test-master-password");
        store.PutSession(s, secret);
        Check(!File.ReadAllText(store.FilePath).Contains(secret), "credentials are encrypted on disk");
        var restarted = new Store(store.DirectoryPath);
        restarted.UnlockVault("test-master-password");
        Check(restarted.GetPassword(s.Id) == secret && restarted.Data.Sessions.Single().Name == s.Name, "restart restores session and master-password protected DPAPI credential");
        restarted.PutSession(s with { Name = "Renamed", Group = "工作" }, secret);
        Check(File.Exists(store.FilePath + ".bak"), "atomic save preserves previous backup");
        string export = Path.Combine(root, "export.json"); restarted.Export(export);
        Check(!File.ReadAllText(export).Contains(secret) && !File.ReadAllText(export).Contains(restarted.Data.Credentials[s.Id.ToString()]), "export excludes all credentials");
        restarted.Import(export); Check(restarted.Data.Sessions.Count == 2 && restarted.Data.Sessions.Select(s => s.Id).Distinct().Count() == 2, "import assigns unique IDs");
        Check(restarted.CheckHostKey("LOCALHOST", 22, "SHA256:first", _ => true), "first host key can be confirmed");
        Check(restarted.CheckHostKey("localhost", 22, "SHA256:first", _ => throw new Exception("should not ask")), "known host key needs no prompt");
        bool changed = false; try { restarted.CheckHostKey("localhost", 22, "SHA256:changed", _ => true); } catch (InvalidOperationException) { changed = true; }
        Check(changed, "changed SSH host key is rejected");
        restarted.Delete(s.Id); Check(!restarted.Data.Credentials.ContainsKey(s.Id.ToString()), "deleting session deletes credential");
        restarted.PutSession(s with { SavePassword = false }, secret); Check(restarted.GetPassword(s.Id) == "", "do not save policy removes persisted password");
        var badDir = Path.Combine(root, "invalid"); Directory.CreateDirectory(badDir); string corrupt = "{broken"; File.WriteAllText(Path.Combine(badDir, "settings.json"), corrupt);
        bool refused = false; try { _ = new Store(badDir); } catch { refused = true; }
        Check(refused && File.ReadAllText(Path.Combine(badDir, "settings.json")) == corrupt, "corrupt configuration is preserved and refused");
        var rdp = new Session { Protocol = Protocol.RDP, Host = "::1", Port = 3390, Username = "域\\用户" };
        string config = RdpLauncher.Configuration(rdp);
        Check(config.Contains("[::1]:3390") && !config.Contains("password") && config.Contains("authentication level:i:1"), "RDP file contains no password and keeps server authentication enabled");
    }
    static void Terminal()
    {
        var t = new TerminalScreen(12, 4); t.Feed("abc\rZ"); Check(t.Lines[0][0].Text == "Z" && t.Lines[0][1].Text == "b", "carriage return overwrites cursor position");
        t.Feed("\x1b[2;3H\x1b[31m红"); Check(t.Lines[1][2].Text == "红" && t.Lines[1][3].Text == "" && t.Lines[1][2].Foreground != TerminalScreen.DefaultForeground, "CJK uses two cells with ANSI color");
        t.Feed("\x1b[2J\x1b[Hmain\x1b[?1049hALT\x1b[?1049l"); Check(t.Lines[0][0].Text == "m", "alternate buffer restores primary screen");
        t.Feed("\x1b[3;1Hold\x1b[3;1H\x1b[Knew"); Check(string.Concat(t.Lines[2].Select(c => c.Text)).TrimEnd() == "new", "erase line clears stale content");
        string? reply = null; t.Reply += s => reply = s; t.Feed("\x1b[6n"); Check(reply == "\x1b[3;4R", "terminal reports cursor position");
        t.Feed("\x1b[38;2;10;20;30mX"); Check(t.Lines[2][3].Foreground == System.Drawing.Color.FromArgb(10,20,30), "truecolor SGR");
        t.Feed("\x1b]0;invisible title\aQ"); Check(!t.Text.Contains("invisible title"), "OSC title does not leak into screen");
        t.Resize(20, 8); Check(t.Columns == 20 && t.Rows == 8 && t.Lines[0][0].Text == "m", "resize preserves screen contents");
        t.Feed("\x1b[?2004h\x1b[?1h"); Check(t.BracketedPaste && t.ApplicationCursor, "bracketed paste and application cursor modes");
        t.Feed("\ud83d"); t.Feed("\ude00"); Check(t.Text.Contains("😀"), "split surrogate pair handled");
        var random = new Random(42); for (int i = 0; i < 2000; i++) t.Feed($"\x1b[{random.Next(65535)};{random.Next(65535)}{new[]{'H','J','K','m','P','@','L','M','r'}[random.Next(9)]}");
        Check(t.X < t.Columns && t.Y < t.Rows, "malformed and oversized ANSI parameters stay bounded");
    }
    static void Telnet()
    {
        var replies = new List<byte[]>(); var codec = new TelnetCodec(replies.Add);
        Check(codec.Decode(new byte[] { 65,255,251 }).SequenceEqual(new byte[] {65}), "Telnet IAC fragmentation preserves text");
        codec.Decode(new byte[] {1}); Check(replies.Single().SequenceEqual(new byte[] {255,253,1}), "Telnet negotiates echo across packet boundary");
        codec.Decode(new byte[] {255,253,24,255,250,24,1,255}); codec.Decode(new byte[] {240});
        Check(replies.Last().Contains((byte)'v'), "Telnet terminal type subnegotiation");
        Check(codec.Decode(new byte[] {255,255,66}).SequenceEqual(new byte[] {255,66}), "Telnet escapes literal IAC");
        Check(TelnetCodec.Encode([13,255]).SequenceEqual(new byte[] {13,0,255,255}), "Telnet NVT CR and outgoing IAC encoding");
    }
    static async Task TelnetRoundTrip(string root)
    {
        var server = new TcpListener(IPAddress.Loopback, 0); server.Start(); int port = ((IPEndPoint)server.LocalEndpoint).Port;
        var text = new StringBuilder(); var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new Session { Protocol = Protocol.Telnet, Host = "127.0.0.1", Port = port };
        using var connection = new TerminalConnection(session, new Store(Path.Combine(root,"telnet")), "", _=>false);
        connection.Output += s => { lock (text) { text.Append(s); if (text.ToString().Contains("hello中文")) done.TrySetResult(true); } };
        Task run = connection.Run(80,24); using var peer = await server.AcceptTcpClientAsync(); var stream = peer.GetStream();
        await stream.WriteAsync(new byte[] {255,251}); await stream.WriteAsync(new byte[] {1});
        var bytes = Encoding.UTF8.GetBytes("hello中文"); foreach (var b in bytes) await stream.WriteAsync(new byte[]{b});
        await done.Task.WaitAsync(TimeSpan.FromSeconds(8));
        connection.Send(Encoding.UTF8.GetBytes("xyz\r"));
        var received = new List<byte>(); var buf = new byte[100];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!Encoding.UTF8.GetString(received.ToArray()).Contains("xyz\r\0")) { int n = await stream.ReadAsync(buf,timeout.Token); received.AddRange(buf.Take(n)); }
        Check(received.Take(3).SequenceEqual(new byte[]{255,253,1}), "live Telnet loopback negotiates and preserves ordered input");
        peer.Dispose(); server.Stop(); await run.WaitAsync(TimeSpan.FromSeconds(8));
    }
    static async Task Shell(string root)
    {
        var s = new Session { Protocol = Protocol.Shell, Executable = "cmd.exe", Arguments = "/Q", WorkingDirectory = root };
        using var connection = new TerminalConnection(s, new Store(Path.Combine(root,"shell")), "", _=>false);
        var text = new StringBuilder(); var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Output += value => { lock(text) { text.Append(value); if (text.ToString().Contains("CONPTY_OK")) done.TrySetResult(true); } };
        var run = connection.Run(80,24);
        connection.Send(Encoding.UTF8.GetBytes("echo CONPTY^_OK\r"));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(12)); connection.Resize(100,30);
        connection.Send(Encoding.UTF8.GetBytes("exit\r")); await run.WaitAsync(TimeSpan.FromSeconds(12));
        Check(text.ToString().Contains("CONPTY_OK"), "real interactive ConPTY shell input/output/resize/exit");
    }
    static async Task FileDiagnostics(string root)
    {
        var backend = new MemoryRemoteFileSystem { ConnectError = new SocketException((int)SocketError.NetworkUnreachable) };
        await using IFileTransfer connection = new FileConnection(Protocol.FTP, backend);
        var diagnostics = new List<ConnectionDiagnostic>(); connection.Diagnostic += diagnostics.Add;
        try { await connection.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1)); } catch (SocketException) { }
        Check(connection.OperationId != Guid.Empty && diagnostics.Any(d => d.State == ConnectionState.Failed) && diagnostics.Any(d => d.State == ConnectionState.Closed), "file connection diagnostics report failed and closed lifecycle");
        var failure = diagnostics.First(d => d.State == ConnectionState.Failed);
        var rendered = DiagnosticPresenter.Error(failure with { Message = "endpoint.invalid/private/password" });
        Check(!rendered.Contains("endpoint.invalid") && !rendered.Contains("password") && rendered.Contains("操作编号"), "diagnostic presenter hides endpoint details");
    }

    static async Task Lifecycle(string root)
    {
        using var connection = new TerminalConnection(new Session { Protocol = Protocol.Shell, Executable="no-such-portharbor-program.exe" },new Store(Path.Combine(root,"invalid-shell")),"",_=>false);
        var diagnostics = new List<ConnectionDiagnostic>(); connection.Diagnostic += diagnostics.Add;
        bool failed=false; try { await connection.Run(80,24).WaitAsync(TimeSpan.FromSeconds(8)); } catch(System.ComponentModel.Win32Exception) { failed=true; }
        Check(failed,"invalid executable fails cleanly without leaked startup state");
        Check(connection.OperationId != Guid.Empty && diagnostics.Any(d => d.State == ConnectionState.Failed && d.ErrorCode == ConnectionErrorCode.PlatformUnavailable) && connection.State == ConnectionState.Closed, "connection diagnostics report operation, stable error and closed state");
    }
    static async Task Integration(string root, string configPath)
    {
        using var config=System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath)); var c=config.RootElement;
        foreach (var protocol in new[]{Protocol.FTP,Protocol.SFTP})
        {
            var s=new Session { Protocol=protocol, Host="127.0.0.1", Port=c.GetProperty(protocol.ToString().ToLowerInvariant()+"Port").GetInt32(), Username="easytest" };
            var store=new Store(Path.Combine(root,protocol.ToString()));
            await using IFileTransfer files=FileTransferFactory.Create(s,store,"easytest-only",_=>true);
            using var timeout = new CancellationTokenSource();
            await files.ConnectAsync(timeout.Token, TimeSpan.FromSeconds(15));
            string local=Path.Combine(root,"upload-"+protocol+".txt"),download=local+".download"; File.WriteAllText(local,"文件传输 test \0\n"+new string('x',200000));
            if (await files.ExistsAsync("/test-dir",timeout.Token,TimeSpan.FromSeconds(10))) { foreach(var old in await files.ListAsync("/test-dir",timeout.Token,TimeSpan.FromSeconds(10))) await files.DeleteAsync(old,timeout.Token,TimeSpan.FromSeconds(10)); await files.DeleteAsync(new("test-dir","/test-dir",true,0,DateTime.Now),timeout.Token,TimeSpan.FromSeconds(10)); }
            await files.MkdirAsync("/test-dir",timeout.Token,TimeSpan.FromSeconds(10));
            await files.UploadAsync(local,"/test-dir/中文.txt",timeout.Token,TimeSpan.FromSeconds(30));
            Check((await files.ListAsync("/test-dir",timeout.Token,TimeSpan.FromSeconds(10))).Any(f=>f.Name=="中文.txt"),protocol+" lists uploaded Unicode filename");
            Check(Throws<IOException>(() => files.DeleteAsync(new("test-dir","/test-dir",true,0,DateTime.Now),timeout.Token,TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()) && await files.ExistsAsync("/test-dir/中文.txt",timeout.Token,TimeSpan.FromSeconds(10)), protocol+" refuses nonempty directory deletion");
            await files.UploadAsync(local,"/test-dir/existing.txt",timeout.Token,TimeSpan.FromSeconds(30));
            Check(Throws<IOException>(() => files.RenameAsync("/test-dir/中文.txt","/test-dir/existing.txt",timeout.Token,TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()) && await files.ExistsAsync("/test-dir/中文.txt",timeout.Token,TimeSpan.FromSeconds(10)), protocol+" refuses rename over existing target");
            await files.DeleteAsync(new("existing.txt","/test-dir/existing.txt",false,0,DateTime.Now),timeout.Token,TimeSpan.FromSeconds(10));
            await files.DownloadAsync("/test-dir/中文.txt",download,timeout.Token,TimeSpan.FromSeconds(30));
            Check(File.ReadAllBytes(local).SequenceEqual(File.ReadAllBytes(download)),protocol+" upload/download bytes round trip");
            using (var stop = new CancellationTokenSource())
            {
                await CheckThrowsAsync<OperationCanceledException>(() => files.UploadAsync(local,"/test-dir/中文.txt",stop.Token,TimeSpan.FromSeconds(30),new CallbackProgress<TransferProgress>(_=>stop.Cancel())))
                    .ConfigureAwait(false);
                Check((await files.ListAsync("/test-dir",timeout.Token,TimeSpan.FromSeconds(10))).Count==1, protocol+" cancelled upload removes remote temporary file");
                await files.DownloadAsync("/test-dir/中文.txt",download,timeout.Token,TimeSpan.FromSeconds(30));
                Check(File.ReadAllBytes(local).SequenceEqual(File.ReadAllBytes(download)), protocol+" cancelled upload retains previous remote content");
            }
            using (var stop = new CancellationTokenSource())
            {
                File.WriteAllText(download,"preserved local target");
                await CheckThrowsAsync<OperationCanceledException>(() => files.DownloadAsync("/test-dir/中文.txt",download,stop.Token,TimeSpan.FromSeconds(30),new CallbackProgress<TransferProgress>(_=>stop.Cancel())))
                    .ConfigureAwait(false);
                Check(File.ReadAllText(download)=="preserved local target" && !Directory.EnumerateFiles(root,"*.part").Any(), protocol+" cancelled download preserves local target and removes temporary file");
            }
            File.WriteAllText(local,"replacement"); await files.UploadAsync(local,"/test-dir/中文.txt",timeout.Token,TimeSpan.FromSeconds(30));
            await files.DownloadAsync("/test-dir/中文.txt",download,timeout.Token,TimeSpan.FromSeconds(30)); Check(File.ReadAllText(download)=="replacement",protocol+" overwrite path");
            await files.RenameAsync("/test-dir/中文.txt","/test-dir/renamed.txt",timeout.Token,TimeSpan.FromSeconds(10)); var file=(await files.ListAsync("/test-dir",timeout.Token,TimeSpan.FromSeconds(10))).Single();
            await files.DeleteAsync(file,timeout.Token,TimeSpan.FromSeconds(10)); await files.DeleteAsync(new("test-dir","/test-dir",true,0,DateTime.Now),timeout.Token,TimeSpan.FromSeconds(10));
            Check(!await files.ExistsAsync("/test-dir",timeout.Token,TimeSpan.FromSeconds(10)),protocol+" rename and delete empty directory");
        }
    }

}

