using FluentFTP;
using Renci.SshNet;

namespace EasyConnect;

public sealed record RemoteEntry(string Name, string Path, bool Directory, long Size, DateTime Modified);
public sealed class FileConnection : IDisposable
{
    readonly Session session; readonly Store store; readonly string password; readonly Func<string, bool> confirm;
    SftpClient? sftp; FtpClient? ftp; PrivateKeyFile? key;
    public FileConnection(Session s, Store store, string password, Func<string, bool> confirm) { session = s; this.store = store; this.password = password; this.confirm = confirm; }
    public void Connect()
    {
        if (session.Protocol == Protocol.SFTP)
        {
            sftp = new SftpClient(TerminalConnection.SshInfo(session, password, out key));
            TerminalConnection.HostKey(sftp, session, store, confirm); sftp.OperationTimeout = TimeSpan.FromSeconds(30); sftp.Connect();
        }
        else
        {
            ftp = new FtpClient(session.Host, session.Username, password, session.Port);
            ftp.Config.EncryptionMode = session.UseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;
            ftp.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
            ftp.Config.ConnectTimeout = 15000; ftp.Config.ReadTimeout = 30000; ftp.Config.DataConnectionReadTimeout = 30000;
            // Default certificate checks remain enabled. Never accept every certificate.
            ftp.Connect();
        }
    }
    public List<RemoteEntry> List(string path) => sftp != null
        ? sftp.ListDirectory(path).Where(f => f.Name is not ("." or "..")).Select(f => new RemoteEntry(f.Name, f.FullName, f.IsDirectory, f.Length, f.LastWriteTime)).ToList()
        : ftp!.GetListing(path).Where(f => f.Name is not ("." or "..")).Select(f => new RemoteEntry(f.Name, f.FullName, f.Type == FtpObjectType.Directory, f.Size, f.Modified)).ToList();
    public bool Exists(string path) => sftp != null ? sftp.Exists(path) : ftp!.FileExists(path) || ftp.DirectoryExists(path);
    public void Download(string remote, string local, CancellationToken token, Action<long> progress)
    {
        using var source = sftp != null ? (Stream)sftp.OpenRead(remote) : ftp!.OpenRead(remote);
        CopyAtomically(source, local, token, progress);
    }
    public static void CopyAtomically(Stream source, string destination, CancellationToken token, Action<long> progress)
    {
        string temp = destination + ".easyconnect-" + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) Copy(source, target, token, progress);
            token.ThrowIfCancellationRequested(); File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Upload(string local, string remote, CancellationToken token, Action<long> progress)
    {
        // Upload to a distinct temporary name so cancellation never truncates the destination.
        string temp = remote + ".easyconnect-" + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var source = File.OpenRead(local))
            using (var target = sftp != null ? (Stream)sftp.OpenWrite(temp) : ftp!.OpenWrite(temp)) Copy(source, target, token, progress);
            token.ThrowIfCancellationRequested();
            if (sftp != null)
            {
                // POSIX atomic replacement is extension-dependent; do not delete an existing file first.
                if (sftp.Exists(remote))
                {
                    try { sftp.RenameFile(temp, remote, true); }
                    catch (NotSupportedException)
                    {
                        // Servers without POSIX rename: retain the old file until replacement succeeds.
                        string backup = remote + ".easyconnect-" + Guid.NewGuid().ToString("N") + ".bak";
                        sftp.RenameFile(remote, backup);
                        try { sftp.RenameFile(temp, remote); }
                        catch { if (!sftp.Exists(remote)) sftp.RenameFile(backup, remote); throw; }
                        try { sftp.DeleteFile(backup); } catch { /* preserve backup if server cleanup fails */ }
                    }
                }
                else sftp.RenameFile(temp, remote);
            }
            else if (!ftp!.MoveFile(temp, remote, FtpRemoteExists.Overwrite)) throw new IOException("远程重命名失败。");
        }
        finally
        {
            try { if (sftp != null && sftp.Exists(temp)) sftp.DeleteFile(temp); else if (ftp != null && ftp.FileExists(temp)) ftp.DeleteFile(temp); } catch { /* disconnected; remote .part can be removed on reconnect */ }
        }
    }
    static void Copy(Stream source, Stream destination, CancellationToken token, Action<long> progress)
    {
        var buffer = new byte[65536]; long done = 0;
        while (true) { token.ThrowIfCancellationRequested(); int n = source.Read(buffer); if (n == 0) break; destination.Write(buffer, 0, n); done += n; progress(done); }
        destination.Flush();
    }
    public void Mkdir(string path) { if (sftp != null) sftp.CreateDirectory(path); else ftp!.CreateDirectory(path); }
    public void Rename(string source, string dest) { if (Exists(dest)) throw new IOException("目标名称已经存在。"); if (sftp != null) sftp.RenameFile(source, dest); else ftp!.Rename(source, dest); }
    public void Delete(RemoteEntry f) { if (sftp != null) { if (f.Directory) sftp.DeleteDirectory(f.Path); else sftp.DeleteFile(f.Path); } else { if (f.Directory) { if (ftp!.GetListing(f.Path).Any()) throw new IOException("仅允许删除空目录。"); ftp.DeleteDirectory(f.Path); } else ftp!.DeleteFile(f.Path); } }
    public void Dispose() { sftp?.Dispose(); ftp?.Dispose(); key?.Dispose(); }
}
