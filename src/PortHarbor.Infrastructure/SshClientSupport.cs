using Renci.SshNet;
using System.Security.Cryptography;

namespace PortHarbor;

public static class SshClientSupport
{
    public static ConnectionInfo SshInfo(Session s, string password, out PrivateKeyFile? key)
    {
        key = null;
        AuthenticationMethod auth;
        if (!string.IsNullOrWhiteSpace(s.PrivateKey)) { key = password.Length == 0 ? new PrivateKeyFile(s.PrivateKey) : new PrivateKeyFile(s.PrivateKey, password); auth = new PrivateKeyAuthenticationMethod(s.Username, key); }
        else auth = new PasswordAuthenticationMethod(s.Username, password);
        return new ConnectionInfo(s.Host, s.Port, s.Username, auth) { Timeout = TimeSpan.FromSeconds(15) };
    }
    public static void HostKey(BaseClient client, Session session, IHostKeyTrust hostKeyTrust, Func<string, bool> confirm, Action<ConnectionStage>? reportStage = null)
    {
        client.HostKeyReceived += (_, e) =>
        {
            reportStage?.Invoke(ConnectionStage.HostKeyVerification);
            e.CanTrust = hostKeyTrust.CheckHostKey(session.Host, session.Port, "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('='), confirm);
            if (!e.CanTrust) throw new HostKeyRejectedException("SSH host key was not accepted.");
            reportStage?.Invoke(ConnectionStage.Authenticating);
        };
        client.KeepAliveInterval = TimeSpan.FromSeconds(30);
    }
}
