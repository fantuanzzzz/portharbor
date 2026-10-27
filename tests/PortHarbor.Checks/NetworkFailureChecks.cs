using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static readonly TimeSpan NetworkDeadline = TimeSpan.FromSeconds(10);

    static async Task TerminalNetworkFailures(string root, string configPath)
    {
        // Keep the port bound but not listening: no external DNS or race with an
        // unrelated process taking a recently released ephemeral port is needed.
        using var refused = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        refused.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int refusedPort = ((IPEndPoint)refused.LocalEndPoint!).Port;
        foreach (var protocol in new[] { Protocol.SSH, Protocol.Telnet, Protocol.FTP, Protocol.SFTP })
        {
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "refused-" + protocol), false));
            var session = new Session { Protocol = protocol, Host = "127.0.0.1", Port = refusedPort, Username = "easytest" };
            var events = new ConcurrentQueue<ConnectionDiagnostic>();
            Exception? error = null;
            if (protocol is Protocol.SSH or Protocol.Telnet)
            {
                await using var terminal = TerminalConnectionFactory.Create(session, store, "", _ => false);
                terminal.Diagnostic += events.Enqueue;
                try { await terminal.Run(80, 24).WaitAsync(NetworkDeadline); }
                catch (TimeoutException) { throw; }
                catch (Exception observed) { error = observed; }
                await terminal.CloseAsync().WaitAsync(NetworkDeadline);
                Check(terminal.State == ConnectionState.Closed, protocol + " refused connection releases its terminal resources");
            }
            else
            {
                await using var files = FileTransferFactory.Create(session, store, "", _ => false);
                files.Diagnostic += events.Enqueue;
                try { await files.ConnectAsync(CancellationToken.None, NetworkDeadline); }
                catch (TimeoutException) { throw; }
                catch (Exception observed) { error = observed; }
            }
            var failure = events.SingleOrDefault(e => e.State == ConnectionState.Failed);
            Check(error != null && failure is { Stage: ConnectionStage.Connecting, ErrorCode: ConnectionErrorCode.NetworkUnavailable, Retryable: true }
                && events.Last().State == ConnectionState.Closed && events.All(e => e.OperationId == failure.OperationId)
                && !DiagnosticPresenter.Error(failure).Contains("127.0.0.1"),
                protocol + " refused loopback connection reports a safe retryable network error and closes");
        }

        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        int stalledPort = config.RootElement.GetProperty("stalledPort").GetInt32();
        using (var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "silent-shell-channel"), false)))
        {
            var session = new Session { Protocol = Protocol.SSH, Host = "127.0.0.1", Port = config.RootElement.GetProperty("sftpPort").GetInt32(), Username = "stallchannel" };
            await using var terminal = new TerminalConnection(session, TerminalTransportFactory.Create(session, store, "easytest-only", _ => true),
                DiagnosticClassifier.Classify, connectTimeout: TimeSpan.FromSeconds(2));
            var events = new ConcurrentQueue<ConnectionDiagnostic>(); terminal.Diagnostic += events.Enqueue;
            Exception? failure = null; var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try { await terminal.Run(80, 24).WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (TimeoutException error) { failure = error; }
            await terminal.CloseAsync().WaitAsync(NetworkDeadline);
            Check(File.Exists(Path.Combine(Path.GetDirectoryName(configPath)!, "shell-channel-stalled"))
                && failure != null && elapsed.Elapsed < TimeSpan.FromSeconds(8) && terminal.State == ConnectionState.Closed
                && events.Any(e => e.Stage == ConnectionStage.OpeningChannel && e.ErrorCode == ConnectionErrorCode.Timeout),
                "synchronous SSH shell-channel creation is interrupted by its connect deadline and releases the client");
        }
        using (var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "silent-ssh"), false)))
        {
            await using var files = FileTransferFactory.Create(
                new Session { Protocol = Protocol.SFTP, Host = "127.0.0.1", Port = stalledPort, Username = "easytest" }, store, "easytest-only", _ => true);
            var events = new ConcurrentQueue<ConnectionDiagnostic>(); files.Diagnostic += events.Enqueue;
            Exception? failure = null; var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try { await files.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(2)); }
            catch (TimeoutException error) { failure = error; }
            await files.DisposeAsync().AsTask().WaitAsync(NetworkDeadline); elapsed.Stop();
            Check(failure is TimeoutException && elapsed.Elapsed < TimeSpan.FromSeconds(5)
                && events.Any(e => e.State == ConnectionState.Failed && e.Stage == ConnectionStage.Connecting && e.ErrorCode == ConnectionErrorCode.Timeout)
                && files.State == ConnectionState.Closed,
                "silent SSH peer is cancelled by the configured connect timeout and closed promptly");
        }

        using (var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "dns-host-not-found"), false)))
        {
            string unresolvedHost = "portharbor-" + Guid.NewGuid().ToString("N") + ".invalid";
            await using var files = FileTransferFactory.Create(
                new Session { Protocol = Protocol.SFTP, Host = unresolvedHost, Port = 22, Username = "easytest" }, store, "easytest-only", _ => true);
            var events = new ConcurrentQueue<ConnectionDiagnostic>(); files.Diagnostic += events.Enqueue;
            Exception? failure = null;
            try { await files.ConnectAsync(CancellationToken.None, NetworkDeadline); }
            catch (TimeoutException) { throw; }
            catch (Exception observed) { failure = observed; }
            Check(failure != null && events.Any(e => e.State == ConnectionState.Failed && e.Stage == ConnectionStage.Connecting
                    && e.ErrorCode == ConnectionErrorCode.NetworkUnavailable && e.Retryable)
                && events.Last().State == ConnectionState.Closed
                && !DiagnosticPresenter.Error(events.First(e => e.State == ConnectionState.Failed)).Contains(unresolvedHost),
                "real unresolved .invalid hostname reports a safe network failure and releases the SFTP client");
        }

        using (var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "ftp-refused-password"), false)))
        {
            await using var ftp = FileTransferFactory.Create(
                new Session { Protocol = Protocol.FTP, Host = "127.0.0.1", Port = config.RootElement.GetProperty("ftpPort").GetInt32(), Username = "easytest" },
                store, "private-wrong-password", _ => false);
            var events = new ConcurrentQueue<ConnectionDiagnostic>();
            ftp.Diagnostic += events.Enqueue;
            await CheckThrowsAsync<FluentFTP.Exceptions.FtpAuthenticationException>(() => ftp.ConnectAsync(CancellationToken.None, NetworkDeadline));
            Check(events.Any(e => e.State == ConnectionState.Failed && e.Stage == ConnectionStage.Authenticating
                    && e.ErrorCode == ConnectionErrorCode.AuthenticationFailed && !e.Retryable)
                && events.Last().State == ConnectionState.Closed,
                "live FTP wrong password reports authentication failure rather than a network error and closes");
        }
        int sshPort = config.RootElement.GetProperty("sftpPort").GetInt32();
        foreach (bool drop in new[] { false, true })
        {
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "ssh-disconnect-" + drop), false));
            await using var terminal = TerminalConnectionFactory.Create(
                new Session { Protocol = Protocol.SSH, Host = "127.0.0.1", Port = sshPort, Username = "easytest" },
                store, "easytest-only", _ => true);
            var events = new ConcurrentQueue<ConnectionDiagnostic>();
            terminal.Diagnostic += events.Enqueue;
            var ready = ScriptedTerminalTransport.NewSignal();
            var output = new StringBuilder();
            terminal.Output += text =>
            {
                output.Append(text);
                if (output.ToString().Contains("SSH_TEST_READY")) ready.TrySetResult();
            };
            Task run = terminal.Run(80, 24);
            try
            {
                await ready.Task.WaitAsync(NetworkDeadline);
                terminal.Send(Encoding.UTF8.GetBytes(drop ? "PORTHARBOR_TEST_DROP\n" : "PORTHARBOR_TEST_EXIT\n"));
                Exception? error = null;
                try { await run.WaitAsync(NetworkDeadline); }
                catch (TimeoutException) { throw; }
                catch (Exception observed) { error = observed; }
                await terminal.CloseAsync().WaitAsync(NetworkDeadline);
                var failures = events.Where(e => e.State == ConnectionState.Failed).ToArray();
                Check(terminal.State == ConnectionState.Closed && events.All(e => e.OperationId == terminal.OperationId)
                    && (drop
                        ? error != null && failures.Any(e => e.Stage == ConnectionStage.Reading && e.ErrorCode == ConnectionErrorCode.NetworkUnavailable && e.Retryable)
                        : error == null && failures.Length == 0),
                    drop ? "live SSH transport loss reports a retryable reading network failure and completes cleanup"
                        : "live SSH shell exit completes normally without a spurious network failure");
            }
            finally { await terminal.CloseAsync().WaitAsync(NetworkDeadline); }
        }

        foreach (bool reset in new[] { false, true })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "telnet-disconnect-" + reset), false));
            await using var terminal = TerminalConnectionFactory.Create(
                new Session { Protocol = Protocol.Telnet, Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port }, store, "", _ => false);
            var events = new ConcurrentQueue<ConnectionDiagnostic>();
            terminal.Diagnostic += events.Enqueue;
            var ready = ScriptedTerminalTransport.NewSignal();
            var output = new StringBuilder();
            terminal.Output += text =>
            {
                output.Append(text);
                if (output.ToString().Contains("NETWORK_READY")) ready.TrySetResult();
            };
            Task run = terminal.Run(80, 24);
            try
            {
                using var peer = await listener.AcceptTcpClientAsync().WaitAsync(NetworkDeadline);
                await peer.GetStream().WriteAsync("NETWORK_READY"u8.ToArray());
                await ready.Task.WaitAsync(NetworkDeadline);
                if (reset)
                {
                    // TcpClient.Dispose performs an orderly shutdown first. Close the
                    // socket directly with zero linger to exercise an actual TCP reset.
                    peer.Client.LingerState = new LingerOption(true, 0);
                    peer.Client.Close();
                }
                else peer.Client.Shutdown(SocketShutdown.Send);
                Exception? error = null;
                try { await run.WaitAsync(NetworkDeadline); }
                catch (TimeoutException) { throw; }
                catch (Exception observed) { error = observed; }
                await terminal.CloseAsync().WaitAsync(NetworkDeadline);
                Check(terminal.State == ConnectionState.Closed && events.All(e => e.OperationId == terminal.OperationId)
                    && (reset
                        ? error is IOException && events.Any(e => e.State == ConnectionState.Failed && e.Stage == ConnectionStage.Reading
                            && e.ErrorCode == ConnectionErrorCode.NetworkUnavailable && e.Retryable)
                        : error == null && !events.Any(e => e.State == ConnectionState.Failed)),
                    reset ? "live Telnet connection reset reports a reading network failure and completes cleanup"
                        : "live Telnet orderly EOF completes normally without a spurious network failure");
            }
            finally { await terminal.CloseAsync().WaitAsync(NetworkDeadline); }
        }
    }
}
