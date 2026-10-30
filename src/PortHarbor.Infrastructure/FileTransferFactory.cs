namespace PortHarbor;

public static class FileTransferFactory
{
    public static IFileTransfer Create(Session session, IHostKeyTrust hostKeyTrust, string password, Func<string, bool> confirm, SshAuthenticationPromptHandler? prompt = null)
        => new FileConnection(session.Protocol, RemoteFileSystemFactory.Create(session, hostKeyTrust, password, confirm, prompt), DiagnosticClassifier.Classify, session.Id);
}
