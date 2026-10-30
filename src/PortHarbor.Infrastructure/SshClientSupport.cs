using Renci.SshNet;
using Renci.SshNet.Common;
using System.Security.Cryptography;

namespace PortHarbor;

public static class SshClientSupport
{
    public static ConnectionInfo SshInfo(Session s, string password, out PrivateKeyFile? key)
        => SshInfo(s.Host, s.Port, s.Username, s.PrivateKey, password, out key);

    internal static ConnectionInfo SshInfo(string host, int port, string username, string privateKey, string password, out PrivateKeyFile? key)
        => SshInfo(host, port, username, privateKey, password, out key, null);

    internal static ConnectionInfo SshInfo(string host, int port, string username, string privateKey, string password, out PrivateKeyFile? key, SshAuthenticationPromptHandler? prompt)
    {
        key = null;
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(privateKey))
        {
            key = password.Length == 0 ? new PrivateKeyFile(privateKey) : new PrivateKeyFile(privateKey, password);
            methods.Add(new PrivateKeyAuthenticationMethod(username, key));
        }
        else methods.Add(new PasswordAuthenticationMethod(username, password));
        if (prompt != null) methods.Add(Interactive(username, prompt));
        return new ConnectionInfo(host, port, username, methods.ToArray()) { Timeout = TimeSpan.FromSeconds(15) };
    }

    static KeyboardInteractiveAuthenticationMethod Interactive(string username, SshAuthenticationPromptHandler prompt)
    {
        var method = new KeyboardInteractiveAuthenticationMethod(username);
        method.AuthenticationPrompt += (_, args) => Respond(args, prompt);
        return method;
    }

    internal static void Respond(AuthenticationPromptEventArgs args, SshAuthenticationPromptHandler prompt)
    {
        var challenge = new SshAuthenticationChallenge(args.Instruction,
            args.Prompts.Select(p => new SshAuthenticationPrompt(p.Request, p.IsEchoed)).ToArray());
        var responses = prompt(challenge);
        if (responses == null || responses.Count != args.Prompts.Count) throw new SshAuthenticationCancelledException();
        for (int i = 0; i < args.Prompts.Count; i++) args.Prompts[i].Response = responses[i];
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
