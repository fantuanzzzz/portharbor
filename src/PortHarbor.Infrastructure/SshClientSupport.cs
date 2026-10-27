using Renci.SshNet;
using System.Security.Cryptography;

namespace PortHarbor;

public static class SshClientSupport
{
    public static ConnectionInfo SshInfo(Session s, string password, out PrivateKeyFile? key)
        => SshInfo(s.Host, s.Port, s.Username, s.PrivateKey, password, out key);

    internal static ConnectionInfo SshInfo(string host, int port, string username, string privateKey, string password, out PrivateKeyFile? key)
    {
        key = null;
        AuthenticationMethod auth;
        if (!string.IsNullOrWhiteSpace(privateKey)) { key = password.Length == 0 ? new PrivateKeyFile(privateKey) : new PrivateKeyFile(privateKey, password); auth = new PrivateKeyAuthenticationMethod(username, key); }
        else auth = new PasswordAuthenticationMethod(username, password);
        return new ConnectionInfo(host, port, username, auth) { Timeout = TimeSpan.FromSeconds(15) };
    }
    public static void HostKey(BaseClient client, Session session, IHostKeyTrust hostKeyTrust, Func<string, bool> confirm, Action<ConnectionStage>? reportStage = null)
        => HostKey(client, session.Host, session.Port, hostKeyTrust, confirm, reportStage);

    internal static void HostKey(BaseClient client, string host, int port, IHostKeyTrust hostKeyTrust, Func<string, bool> confirm, Action<ConnectionStage>? reportStage = null)
    {
        client.HostKeyReceived += (_, e) =>
        {
            reportStage?.Invoke(ConnectionStage.HostKeyVerification);
            e.CanTrust = hostKeyTrust.CheckHostKey(host, port, "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('='), confirm);
            if (!e.CanTrust) throw new HostKeyRejectedException("SSH host key was not accepted.");
            reportStage?.Invoke(ConnectionStage.Authenticating);
        };
        client.KeepAliveInterval = TimeSpan.FromSeconds(30);
    }
}
