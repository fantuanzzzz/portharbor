using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static async Task WindowsPlatformAdapters()
    {
        var assembly = typeof(WindowsTerminalTransportFactory).Assembly;
        Check(assembly != typeof(MainForm).Assembly && assembly == typeof(ConPty).Assembly
            && assembly.GetReferencedAssemblies().All(a => a.Name is not ("System.Windows.Forms" or "Renci.SshNet" or "FluentFTP" or "PortHarbor.Infrastructure"))
            && typeof(StreamTerminalTransport).Assembly == typeof(SessionService).Assembly,
            "Windows terminal adapters depend on application ports without UI or network infrastructure");
        await using (var shell = WindowsTerminalTransportFactory.Create(new Session { Protocol = Protocol.Shell }))
        await using (var serial = WindowsTerminalTransportFactory.Create(new Session { Protocol = Protocol.Serial }))
            Check(shell.GetType() != serial.GetType() && shell.GetType().Assembly == assembly && serial.GetType().Assembly == assembly,
                "Windows terminal factory selects separate shell and serial adapters without opening resources");
        Check(Enum.GetValues<Protocol>().Where(p => p is not (Protocol.Serial or Protocol.Shell))
            .All(protocol => Throws<ArgumentException>(() => WindowsTerminalTransportFactory.Create(new Session { Protocol = protocol }))),
            "Windows terminal factory rejects network and nonterminal protocols");
        foreach (var protocol in new[] { Protocol.Shell, Protocol.Serial })
        {
            await using var transport = WindowsTerminalTransportFactory.Create(new Session { Protocol = protocol });
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            int reports = 0;
            await CheckThrowsAsync<OperationCanceledException>(() => transport.ConnectAsync(80, 24, _ => reports++, cancelled.Token));
            Check(reports == 0, protocol + " pre-cancellation prevents Windows adapter resource startup");
        }
    }

    static void SerialProviderUi()
    {
        var provider = new FixtureSerialPortProvider();
        using var editor = new SessionEditor(new Session { Protocol = Protocol.Serial, SerialPort = "COM42", Parity = "Even", StopBits = "Two", Handshake = "XOnXOff" }, serialPorts: provider);
        editor.Show(); Application.DoEvents();
        var boxes = Descendants(editor).OfType<ComboBox>().ToArray();
        Check(provider.Reads == 1 && boxes.Any(b => b.Text == "COM42" && b.Items.Cast<string>().SequenceEqual(new[] { "COM42", "COM99" }))
            && boxes.Any(b => b.Text == "Even") && boxes.Any(b => b.Text == "Two") && boxes.Any(b => b.Text == "XOnXOff"),
            "serial editor uses injected device names and persisted option values without accessing hardware");
        editor.Close();
    }

    static async Task ShellPlatformCancellation(string root)
    {
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "platform-shell-cancel"), false));
        await using var terminal = TerminalConnectionFactory.Create(new Session
            { Protocol = Protocol.Shell, Executable = "cmd.exe", Arguments = "/Q", WorkingDirectory = root }, store, "", _ => false);
        var ready = ScriptedTerminalTransport.NewSignal();
        var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<ConnectionDiagnostic>();
        var text = new System.Text.StringBuilder();
        terminal.Diagnostic += diagnostics.Enqueue;
        terminal.Output += value => { text.Append(value); if (text.ToString().Contains("PLATFORM_READY")) ready.TrySetResult(); };
        Task run = terminal.Run(80, 24);
        terminal.Send(System.Text.Encoding.UTF8.GetBytes("echo PLATFORM^_READY\r"));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(12));
        Task close = terminal.CloseAsync();
        await Task.WhenAll(run, close).WaitAsync(TimeSpan.FromSeconds(10));
        await terminal.CloseAsync();
        Check(terminal.State == ConnectionState.Closed && !diagnostics.Any(d => d.State == ConnectionState.Failed),
            "Windows platform shell close interrupts idle ConPTY I/O and awaits process cleanup");
        using var child = new ConPty(new Session { Protocol = Protocol.Shell, Executable = "cmd.exe", Arguments = "/Q", WorkingDirectory = root }, 80, 24);
        await Task.Run(child.Dispose).WaitAsync(TimeSpan.FromSeconds(10));
        Check(child.Completion.IsCompletedSuccessfully, "ConPTY disposal awaits its owned process exit observer");
    }

    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    sealed class FixtureSerialPortProvider : ISerialPortProvider
    {
        internal int Reads;
        public IReadOnlyList<string> GetPortNames() { Reads++; return ["COM42", "COM99"]; }
        public IReadOnlyList<string> ParityOptions => ["None", "Even"];
        public IReadOnlyList<string> StopBitOptions => ["One", "Two"];
        public IReadOnlyList<string> HandshakeOptions => ["None", "XOnXOff"];
    }
}
