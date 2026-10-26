using System.Net;
using System.Net.Sockets;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static async Task TerminalAdapters()
    {
        Check(typeof(TerminalTransportFactory).Assembly == typeof(Store).Assembly
            && typeof(SshTerminalTransport).Assembly == typeof(Store).Assembly
            && typeof(TelnetTerminalTransport).Assembly == typeof(Store).Assembly,
            "SSH and Telnet terminal adapters live in Infrastructure");
        var backend = new LateOpeningTransport();
        try
        {
            Task opening = backend.ConnectAsync(80, 24, _ => { }, CancellationToken.None);
            await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task disposing = backend.DisposeAsync().AsTask();
            await backend.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!disposing.IsCompleted, "stream adapter disposal waits for startup before releasing its final resources");
            backend.Release.TrySetResult();
            await CheckThrowsAsync<OperationCanceledException>(() => opening);
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
            await backend.DisposeAsync();
            Check(backend.Resource.DisposeCalls == 1, "stream resources created after abort are released once and cannot escape startup");
        }
        finally { backend.Release.TrySetResult(); await backend.DisposeAsync(); }
    }

    sealed class LateOpeningTransport : StreamTerminalTransport
    {
        internal readonly TaskCompletionSource Started = ScriptedTerminalTransport.NewSignal(), Release = ScriptedTerminalTransport.NewSignal(), Cancelled = ScriptedTerminalTransport.NewSignal();
        internal readonly CountedResource Resource = new();
        protected override async Task OpenAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token)
        {
            using var registration = token.Register(() => Cancelled.TrySetResult());
            Started.TrySetResult();
            await Release.Task;
            Own(Resource);
        }
    }
    sealed class CountedResource : IDisposable
    {
        internal int DisposeCalls;
        public void Dispose() => Interlocked.Increment(ref DisposeCalls);
    }

    static async Task TerminalNetworkCancellation(string root)
    {
        foreach (var protocol in new[] { Protocol.SSH, Protocol.Telnet })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "terminal-cancel-" + protocol), false));
            var session = new Session { Protocol = protocol, Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, Username = "easytest" };
            await using var terminal = TerminalConnectionFactory.Create(session, store, "", _ => false);
            var events = new System.Collections.Concurrent.ConcurrentQueue<ConnectionDiagnostic>(); terminal.Diagnostic += events.Enqueue;
            var reading = ScriptedTerminalTransport.NewSignal();
            terminal.Output += _ => reading.TrySetResult();
            using var token = new CancellationTokenSource();
            Task run = terminal.Run(80, 24, token.Token);
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
            if (await Task.WhenAny(run, accepting).WaitAsync(TimeSpan.FromSeconds(5)) == run) await run;
            using var peer = await accepting.WaitAsync(TimeSpan.FromSeconds(5));
            // SSH deliberately receives no banner; Telnet is left waiting for its first bytes.
            if (protocol == Protocol.Telnet) await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
            token.Cancel();
            await Task.WhenAll(run, terminal.CloseAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            Check(terminal.State == ConnectionState.Closed && !events.Any(e => e.State == ConnectionState.Failed)
                && events.Any(e => e.ErrorCode == ConnectionErrorCode.Cancelled),
                protocol + " cancellation interrupts a stalled network terminal and awaits its cleanup");
        }
    }
}
