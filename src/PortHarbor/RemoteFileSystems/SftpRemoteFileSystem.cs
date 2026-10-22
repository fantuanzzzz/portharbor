using Renci.SshNet;

namespace PortHarbor;

internal sealed class SftpRemoteFileSystem(Session session, Store store, string password, Func<string, bool> confirm) : IRemoteFileSystem
{
    SftpClient? client;
    PrivateKeyFile? key;
    SftpClient Client => client ?? throw new InvalidOperationException("SFTP 尚未连接。");

    public void Connect(Action<ConnectionStage> reportStage)
    {
        reportStage(ConnectionStage.Authenticating);
        client = new SftpClient(TerminalConnection.SshInfo(session, password, out key));
        reportStage(ConnectionStage.HostKeyVerification);
        TerminalConnection.HostKey(client, session, store, confirm);
        client.OperationTimeout = TimeSpan.FromSeconds(30);
        client.Connect();
    }

    public List<RemoteEntry> List(string path) => Client.ListDirectory(path)
        .Where(f => f.Name is not ("." or ".."))
        .Select(f => new RemoteEntry(f.Name, f.FullName, f.IsDirectory, f.Length, f.LastWriteTime)).ToList();

    public bool Exists(string path) => Client.Exists(path);
    public Stream OpenRead(string path) => Client.OpenRead(path);
    public Stream OpenWrite(string path) => Client.OpenWrite(path);

    public void CommitUpload(string temporaryPath, string destination)
    {
        if (!Client.Exists(destination))
        {
            Client.RenameFile(temporaryPath, destination);
            return;
        }

        try { Client.RenameFile(temporaryPath, destination, true); }
        catch (NotSupportedException)
        {
            // Preserve the old destination if the server lacks POSIX overwrite support.
            string backup = destination + ".portharbor-" + Guid.NewGuid().ToString("N") + ".bak";
            Client.RenameFile(destination, backup);
            try { Client.RenameFile(temporaryPath, destination); }
            catch
            {
                if (!Client.Exists(destination)) Client.RenameFile(backup, destination);
                throw;
            }
            // A failed backup cleanup must not turn a successful upload into a retry.
            try { Client.DeleteFile(backup); } catch { }
        }
    }

    public void DeleteFile(string path) => Client.DeleteFile(path);
    public void Mkdir(string path) => Client.CreateDirectory(path);
    public void Rename(string source, string destination) => Client.RenameFile(source, destination);

    public void Delete(RemoteEntry entry)
    {
        // Use the same empty-directory rule for both protocols.
        if (entry.Directory)
        {
            if (List(entry.Path).Count != 0) throw new IOException("仅允许删除空目录。");
            Client.DeleteDirectory(entry.Path);
        }
        else Client.DeleteFile(entry.Path);
    }

    public void Dispose()
    {
        var current = client;
        var currentKey = key;
        client = null;
        key = null;
        try { current?.Dispose(); }
        finally { currentKey?.Dispose(); }
    }
}
