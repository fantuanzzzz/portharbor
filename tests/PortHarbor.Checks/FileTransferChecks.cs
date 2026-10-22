using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    static void FileTransferContract(string root)
    {
        var store = new Store(Path.Combine(root, "file-contract"));
        foreach (var protocol in Enum.GetValues<Protocol>().Where(p => p is not (Protocol.FTP or Protocol.SFTP)))
            Check(Throws<ArgumentException>(() => FileTransferFactory.Create(new Session { Protocol = protocol }, store, "", _ => false)), "file transfer factory rejects " + protocol);
        using (var ftp = RemoteFileSystemFactory.Create(new Session { Protocol = Protocol.FTP }, store, "", _ => false))
        using (var sftp = RemoteFileSystemFactory.Create(new Session { Protocol = Protocol.SFTP }, store, "", _ => false))
            Check(ftp is FtpRemoteFileSystem && sftp is SftpRemoteFileSystem, "FTP and SFTP select independent adapters without connecting");

        foreach (var protocol in new[] { Protocol.FTP, Protocol.SFTP })
        {
            var backend = new MemoryRemoteFileSystem();
            using IFileTransfer transfer = new FileConnection(protocol, backend);
            var diagnostics = new List<ConnectionDiagnostic>();
            transfer.Diagnostic += diagnostics.Add;
            Check(Throws<InvalidOperationException>(() => transfer.List("/")) && backend.ListCalls == 0, protocol + " rejects file operations before connection");
            transfer.Connect(); transfer.Connect();
            Check(transfer.State == ConnectionState.Connected && backend.ConnectCalls == 1, protocol + " connects once through the shared contract");
            byte[] content = Enumerable.Range(0, 300000).Select(i => (byte)(i % 251)).ToArray();
            string local = Path.Combine(root, "contract-upload-" + protocol + ".bin");
            string download = local + ".download";
            File.WriteAllBytes(local, content);
            backend.Files["/data.bin"] = [42];
            var progress = new List<long>();
            transfer.Upload(local, "/data.bin", CancellationToken.None, progress.Add);
            Check(backend.Files["/data.bin"].SequenceEqual(content) && backend.Files.Count == 1 && backend.CommitCalls == 1 && !backend.WriteOpen, protocol + " uploads through a closed temporary stream before commit");
            Check(progress.Count > 1 && progress[^1] == content.Length && progress.Zip(progress.Skip(1)).All(p => p.First < p.Second), protocol + " reports cumulative upload progress");
            transfer.Download("/data.bin", download, CancellationToken.None, _ => { });
            Check(File.ReadAllBytes(download).SequenceEqual(content) && backend.OpenReads == 1, protocol + " downloads bytes through shared transfer policy");

            using var cancelUpload = new CancellationTokenSource();
            Check(Throws<OperationCanceledException>(() => transfer.Upload(local, "/data.bin", cancelUpload.Token, _ => cancelUpload.Cancel()))
                && backend.Files.Count == 1 && backend.Files["/data.bin"].SequenceEqual(content) && backend.CommitCalls == 1,
                protocol + " cancelled upload preserves destination and removes temporary file");
            using var cancelDownload = new CancellationTokenSource();
            File.WriteAllText(download, "original");
            Check(Throws<OperationCanceledException>(() => transfer.Download("/data.bin", download, cancelDownload.Token, _ => cancelDownload.Cancel()))
                && File.ReadAllText(download) == "original" && !Directory.EnumerateFiles(root, "*.part").Any(),
                protocol + " cancelled download preserves local target and cleans temporary file");
            Check(diagnostics.Last().ErrorCode == ConnectionErrorCode.Cancelled && diagnostics.Last().OperationId == transfer.OperationId
                && diagnostics.Last().Protocol == protocol, protocol + " cancellation diagnostic preserves operation and protocol identity");

            using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
            int reads = backend.OpenReads, writes = backend.OpenWrites;
            Check(Throws<OperationCanceledException>(() => transfer.Upload(local, "/data.bin", preCancelled.Token, _ => { }))
                && Throws<OperationCanceledException>(() => transfer.Download("/data.bin", download, preCancelled.Token, _ => { }))
                && backend.OpenReads == reads && backend.OpenWrites == writes, protocol + " pre-cancelled transfers never open protocol streams");

            backend.CommitError = new IOException("commit failed");
            Check(Throws<IOException>(() => transfer.Upload(local, "/data.bin", CancellationToken.None, _ => { }))
                && backend.Files.Count == 1 && backend.Files["/data.bin"].SequenceEqual(content), protocol + " failed commit cleans temporary file and retains old destination");
            backend.CommitError = null;

            backend.ExistsError = new UnauthorizedAccessException("private endpoint");
            Check(Throws<UnauthorizedAccessException>(() => transfer.Exists("/data.bin")) && diagnostics.Last().Stage == ConnectionStage.Reading
                && diagnostics.Last().ErrorCode == ConnectionErrorCode.PermissionDenied && !diagnostics.Last().Message.Contains("private endpoint"),
                protocol + " existence failure emits a safe reading diagnostic");
            backend.ExistsError = null;

            backend.Files["/existing.bin"] = [99];
            Check(Throws<IOException>(() => transfer.Rename("/data.bin", "/existing.bin")) && backend.RenameCalls == 0
                && backend.Files["/existing.bin"][0] == 99, protocol + " rename refuses overwriting an existing destination");
            transfer.Rename("/data.bin", "/renamed.bin");
            transfer.Mkdir("/new-directory");
            var entry = transfer.List("/").Single(e => e.Name == "renamed.bin");
            transfer.Delete(entry);
            Check(!transfer.Exists("/renamed.bin") && backend.Directories.Contains("/new-directory"), protocol + " lists renames creates and deletes through the shared contract");
            transfer.Dispose(); transfer.Dispose();
            Check(backend.DisposeCalls == 1 && transfer.State == ConnectionState.Closed && Throws<ObjectDisposedException>(transfer.Connect),
                protocol + " repeated disposal closes adapter once and prevents reconnect");
        }

        var retryBackend = new MemoryRemoteFileSystem { ConnectError = new IOException("connect failed") };
        using IFileTransfer retry = new FileConnection(Protocol.SFTP, retryBackend);
        Check(Throws<IOException>(retry.Connect) && retryBackend.DisposeCalls == 1 && retry.State == ConnectionState.Closed, "failed connection releases adapter resources");
        retryBackend.ConnectError = null; retry.Connect();
        Check(retry.State == ConnectionState.Connected && retryBackend.ConnectCalls == 2, "connection failure can be explicitly retried");

        var cleanupBackend = new MemoryRemoteFileSystem { CommitError = new IOException("original commit error"), CleanupError = true };
        using IFileTransfer cleanup = new FileConnection(Protocol.FTP, cleanupBackend); cleanup.Connect();
        string source = Path.Combine(root, "cleanup-source.bin"); File.WriteAllBytes(source, [1, 2, 3]);
        Exception? original = null;
        try { cleanup.Upload(source, "/data.bin", CancellationToken.None, _ => { }); } catch (IOException error) { original = error; }
        Check(ReferenceEquals(original, cleanupBackend.CommitError), "best-effort remote cleanup does not replace the transfer error");
    }

    static void FileTransferUi()
    {
        var backend = new MemoryRemoteFileSystem();
        backend.Files["/测试文件.txt"] = [1, 2, 3];
        using var form = new Form { Width = 960, Height = 640 };
        var view = new FileView(new FileConnection(Protocol.SFTP, backend), "/") { Dock = DockStyle.Fill };
        form.Controls.Add(view); form.Show();
        var listing = view.Controls.OfType<ListView>().Single();
        var status = view.Controls.OfType<Label>().Single();
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while ((listing.Items.Count == 0 || status.Text != "就绪") && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(listing.Items.Count == 1 && listing.Items[0].Text == "测试文件.txt" && status.Text == "就绪",
            "file view loads Unicode entries through injected transfer interface");
        form.Close(); Application.DoEvents();
        Check(backend.DisposeCalls == 1, "closing file view releases its injected transfer");
    }

    // In-memory adapter exercises shared policy without servers or vendor clients.
    sealed class MemoryRemoteFileSystem : IRemoteFileSystem
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public readonly HashSet<string> Directories = [];
        public Exception? ConnectError, ExistsError, CommitError;
        public bool CleanupError, WriteOpen;
        public int ConnectCalls, ListCalls, OpenReads, OpenWrites, CommitCalls, RenameCalls, DisposeCalls;
        public void Connect(Action<ConnectionStage> reportStage)
        {
            ConnectCalls++; reportStage(ConnectionStage.Authenticating);
            if (ConnectError != null) throw ConnectError;
        }
        public List<RemoteEntry> List(string path)
        {
            ListCalls++;
            return Files.Select(f => new RemoteEntry(f.Key.TrimStart('/'), f.Key, false, f.Value.Length, DateTime.UnixEpoch)).ToList();
        }
        public bool Exists(string path)
        {
            if (ExistsError != null) throw ExistsError;
            return Files.ContainsKey(path) || Directories.Contains(path);
        }
        public Stream OpenRead(string path) { OpenReads++; return new MemoryStream(Files[path], false); }
        public Stream OpenWrite(string path)
        {
            OpenWrites++; WriteOpen = true;
            return new SavedStream(bytes => { Files[path] = bytes; WriteOpen = false; });
        }
        public void CommitUpload(string temporaryPath, string destination)
        {
            if (WriteOpen) throw new InvalidOperationException("commit before stream close");
            CommitCalls++;
            if (CommitError != null) throw CommitError;
            Files[destination] = Files[temporaryPath]; Files.Remove(temporaryPath);
        }
        public void DeleteFile(string path)
        {
            if (CleanupError) throw new IOException("cleanup failed");
            Files.Remove(path);
        }
        public void Mkdir(string path) => Directories.Add(path);
        public void Rename(string source, string destination) { RenameCalls++; Files[destination] = Files[source]; Files.Remove(source); }
        public void Delete(RemoteEntry entry) { if (entry.Directory) Directories.Remove(entry.Path); else Files.Remove(entry.Path); }
        public void Dispose() { DisposeCalls++; }

        sealed class SavedStream(Action<byte[]> save) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing) save(ToArray());
                base.Dispose(disposing);
            }
        }
    }
}
