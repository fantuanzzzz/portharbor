using System.Net.Sockets;
using PortHarbor;
using Renci.SshNet.Common;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void DiagnosticClassification(string root)
    {
        Check(DiagnosticClassifier.Classify(new SshAuthenticationException("private authentication details"), ConnectionStage.HostKeyVerification) == ConnectionErrorCode.AuthenticationFailed,
            "SSH authentication failure is not misclassified as host key rejection");
        Check(DiagnosticClassifier.Classify(new SshOperationTimeoutException("private timeout details"), ConnectionStage.Authenticating) == ConnectionErrorCode.Timeout,
            "SSH timeout keeps its timeout category during authentication");
        Check(DiagnosticClassifier.Classify(new IOException("private endpoint", new SocketException((int)SocketError.ConnectionReset)), ConnectionStage.Reading) == ConnectionErrorCode.NetworkUnavailable,
            "wrapped socket failure is classified without parsing exception text");
        Check(DiagnosticClassifier.Classify(new HostKeyRejectedException("private host key"), ConnectionStage.Connecting) == ConnectionErrorCode.HostKeyRejected,
            "explicit host key rejection keeps its category regardless of stage");
        Check(DiagnosticClassifier.Classify(new SshException("private protocol details"), ConnectionStage.Connecting) == ConnectionErrorCode.ProtocolError,
            "SSH handshake failure is classified as a protocol error");

        foreach (var protocol in Enum.GetValues<Protocol>())
        {
            var diagnostic = new ConnectionDiagnostic(Guid.NewGuid(), protocol, ConnectionState.Failed,
                ConnectionStage.Authenticating, ConnectionErrorCode.AuthenticationFailed, false, "secret-password/private-host/private-path\x1b[31m");
            string text = DiagnosticPresenter.Error(diagnostic);
            Check(text.Contains("身份验证失败") && text.Contains(diagnostic.OperationId.ToString("N")[..8]) && !text.Contains("private") && !text.Contains("secret") && !text.Contains('\x1b'),
                protocol + " diagnostic presentation excludes raw messages and terminal control characters");
        }
        var closed = new ConnectionDiagnostic(Guid.NewGuid(), Protocol.SSH, ConnectionState.Closed, ConnectionStage.Closing, ConnectionErrorCode.None, false, "");
        Check(DiagnosticPresenter.Status(closed) == "已关闭", "closed lifecycle is presented as closed rather than closing");
    }

    static async Task TerminalDiagnostics(string root)
    {
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "terminal-diagnostics"), false));
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var connection = TerminalConnectionFactory.Create(new Session { Protocol = Protocol.Telnet, Host = "127.0.0.1", Port = port }, store, "", _ => false);
        var events = new List<ConnectionDiagnostic>(); connection.Diagnostic += events.Add;
        await CheckThrowsAsync<SocketException>(() => connection.Run(80, 24).WaitAsync(TimeSpan.FromSeconds(10)));
        Check(events.Any(e => e.State == ConnectionState.Failed && e.Stage == ConnectionStage.Connecting && e.ErrorCode == ConnectionErrorCode.NetworkUnavailable)
            && events.All(e => e.OperationId == connection.OperationId) && connection.State == ConnectionState.Closed,
            "refused Telnet connection reports network failure and completes cleanup");
        using var invalid = TerminalConnectionFactory.Create(new Session { Host = "" }, store, "", _ => false);
        ConnectionDiagnostic? validation = null;
        invalid.Diagnostic += e => { if (e.State == ConnectionState.Failed) validation = e; };
        await CheckThrowsAsync<ArgumentException>(() => invalid.Run(80, 24));
        Check(validation is { Stage: ConnectionStage.Validation, ErrorCode: ConnectionErrorCode.InvalidConfiguration },
            "terminal validates configuration before protocol startup");
    }

    static async Task SshFailureDiagnostics(string root, string configPath)
    {
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        var session = new Session { Protocol = Protocol.SSH, Host = "127.0.0.1", Port = config.RootElement.GetProperty("sftpPort").GetInt32(), Username = "easytest" };
        foreach (var mode in new[] { "wrong-password", "reject-key", "changed-key" })
        {
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "ssh-failure-" + mode), false));
            if (mode == "changed-key") store.CheckHostKey(session.Host, session.Port, "SHA256:old-key", _ => true);
            using var connection = TerminalConnectionFactory.Create(session, store, mode == "wrong-password" ? "private-wrong-password" : "easytest-only", _ => mode != "reject-key");
            var events = new List<ConnectionDiagnostic>(); connection.Diagnostic += events.Add;
            bool failed = false;
            try { await connection.Run(80, 24).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { throw; }
            catch (Exception) { failed = true; }
            var expected = mode == "wrong-password" ? ConnectionErrorCode.AuthenticationFailed : ConnectionErrorCode.HostKeyRejected;
            var expectedStage = mode == "wrong-password" ? ConnectionStage.Authenticating : ConnectionStage.HostKeyVerification;
            Check(failed && events.Any(e => e.State == ConnectionState.Failed && e.ErrorCode == expected && e.Stage == expectedStage) && connection.State == ConnectionState.Closed,
                "live SSH " + mode + " has correct failure category, stage and cleanup");
        }
        using var sftpStore = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "sftp-auth-failure"), false));
        await using var sftp = FileTransferFactory.Create(session with { Protocol = Protocol.SFTP }, sftpStore, "private-wrong-password", _ => true);
        ConnectionDiagnostic? sftpFailure = null;
        sftp.Diagnostic += e => { if (e.State == ConnectionState.Failed) sftpFailure = e; };
        await CheckThrowsAsync<SshAuthenticationException>(() => sftp.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(10)));
        Check(sftpFailure is { Stage: ConnectionStage.Authenticating, ErrorCode: ConnectionErrorCode.AuthenticationFailed },
            "live SFTP authentication failure shares the corrected SSH classification");
    }
}
