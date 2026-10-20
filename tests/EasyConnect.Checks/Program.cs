using System.Net;
using System.Net.Sockets;
using System.Text;
using EasyConnect;

internal static class Program
{
    static int passed;
    static readonly List<string> results = [];
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; results.Add(name); Console.WriteLine("PASS: " + name); }
    [STAThread]
    static async Task<int> Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "EasyConnect-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            if (args.Length > 0 && args[0] == "--ui") { Ui(root, args[1]); return 0; }
            if (args.Length > 0 && args[0] == "--ui-shell") { UiShell.Run(root, args[1]); return 0; }
            Storage(root); Terminal(); Telnet(); await TelnetRoundTrip(root); if (!args.Contains("--skip-shell")) await Shell(root); Transfer(root); await Lifecycle(root);
            if (args.Length >= 2 && args[0] == "--integration") { Integration(root, args[1]); await SshRoundTrip(root,args[1]); }
            Console.WriteLine($"{passed} checks passed");
            int resultIndex = Array.IndexOf(args, "--results");
            string? resultPath = resultIndex >= 0 && resultIndex + 1 < args.Length ? args[resultIndex + 1] : args.Length == 1 && args[0].EndsWith(".json") ? args[0] : null;
            if (resultPath != null) File.WriteAllText(resultPath, System.Text.Json.JsonSerializer.Serialize(new { passed, results, date = DateTimeOffset.Now }, Store.Json));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Ui(string root, string output)
    {
        ApplicationConfiguration.Initialize();
        using var form = new MainForm(new Store(Path.Combine(root,"ui")));
        form.Show(); Application.DoEvents();
        using var bitmap = new System.Drawing.Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,form.ClientRectangle); bitmap.Save(output);
        Check(form.Visible && form.Controls.Count>0,"main window renders without error");
        foreach(var p in Enum.GetValues<Protocol>()) { using var editor = new SessionEditor(new Session { Protocol=p,Port=Session.DefaultPort(p) }); editor.Show(); Application.DoEvents(); editor.Close(); }
        Check(true,"all seven protocol editors open without error"); form.Close();
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
        store.PutSession(s, secret);
        Check(!File.ReadAllText(store.FilePath).Contains(secret), "credentials are encrypted on disk");
        var restarted = new Store(store.DirectoryPath);
        Check(restarted.GetPassword(s.Id) == secret && restarted.Data.Sessions.Single().Name == s.Name, "restart restores session and DPAPI credential");
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
    static void Transfer(string root)
    {
        string dest = Path.Combine(root,"download.bin"); File.WriteAllText(dest,"original");
        using var data = new MemoryStream(new byte[300000]); using var stop = new CancellationTokenSource();
        bool cancelled = false; try { FileConnection.CopyAtomically(data,dest,stop.Token,n => stop.Cancel()); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && File.ReadAllText(dest)=="original" && !Directory.EnumerateFiles(root,"*.part").Any(), "cancelled transfer preserves target and cleans temporary file");
        data.Position=0; FileConnection.CopyAtomically(data,dest,CancellationToken.None,_=>{}); Check(new FileInfo(dest).Length==300000,"completed transfer replaces target");
    }
    static async Task Lifecycle(string root)
    {
        using var connection = new TerminalConnection(new Session { Protocol = Protocol.Shell, Executable="no-such-easyconnect-program.exe" },new Store(Path.Combine(root,"invalid-shell")),"",_=>false);
        bool failed=false; try { await connection.Run(80,24).WaitAsync(TimeSpan.FromSeconds(8)); } catch(System.ComponentModel.Win32Exception) { failed=true; }
        Check(failed,"invalid executable fails cleanly without leaked startup state");
    }
    static void Integration(string root, string configPath)
    {
        using var config=System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath)); var c=config.RootElement;
        foreach (var protocol in new[]{Protocol.FTP,Protocol.SFTP})
        {
            var s=new Session { Protocol=protocol, Host="127.0.0.1", Port=c.GetProperty(protocol.ToString().ToLowerInvariant()+"Port").GetInt32(), Username="easytest" };
            var store=new Store(Path.Combine(root,protocol.ToString()));
            using var files=new FileConnection(s,store,"easytest-only",_=>true); files.Connect();
            string local=Path.Combine(root,"upload-"+protocol+".txt"),download=local+".download"; File.WriteAllText(local,"文件传输 test \0\n"+new string('x',200000));
            if (files.Exists("/test-dir")) { foreach(var old in files.List("/test-dir")) files.Delete(old); files.Delete(new("test-dir","/test-dir",true,0,DateTime.Now)); }
            files.Mkdir("/test-dir"); files.Upload(local,"/test-dir/中文.txt",CancellationToken.None,_=>{});
            Check(files.List("/test-dir").Any(f=>f.Name=="中文.txt"),protocol+" lists uploaded Unicode filename");
            files.Download("/test-dir/中文.txt",download,CancellationToken.None,_=>{});
            Check(File.ReadAllBytes(local).SequenceEqual(File.ReadAllBytes(download)),protocol+" upload/download bytes round trip");
            File.WriteAllText(local,"replacement"); files.Upload(local,"/test-dir/中文.txt",CancellationToken.None,_=>{});
            files.Download("/test-dir/中文.txt",download,CancellationToken.None,_=>{}); Check(File.ReadAllText(download)=="replacement",protocol+" overwrite path");
            files.Rename("/test-dir/中文.txt","/test-dir/renamed.txt"); var file=files.List("/test-dir").Single(); files.Delete(file); files.Delete(new("test-dir","/test-dir",true,0,DateTime.Now));
            Check(!files.Exists("/test-dir"),protocol+" rename and delete empty directory");
        }
    }
}
