using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    static async Task FileTransferContract(string root)
    {
        var store = new Store(Path.Combine(root, "file-contract"));
        foreach (var protocol in Enum.GetValues<Protocol>().Where(p => p is not (Protocol.FTP or Protocol.SFTP)))
            Check(Throws<ArgumentException>(() => FileTransferFactory.Create(new Session { Protocol = protocol }, store, "", _ => false)), "file transfer factory rejects " + protocol);
        await using (var ftp = RemoteFileSystemFactory.Create(new Session { Protocol = Protocol.FTP }, store, "", _ => false))
        await using (var sftp = RemoteFileSystemFactory.Create(new Session { Protocol = Protocol.SFTP }, store, "", _ => false))
            Check(ftp is FtpRemoteFileSystem && sftp is SftpRemoteFileSystem, "FTP and SFTP select independent adapters without connecting");

        foreach (var protocol in new[] { Protocol.FTP, Protocol.SFTP })
        {
            var backend = new MemoryRemoteFileSystem();
            await using IFileTransfer transfer = new FileConnection(protocol, backend);
            var diagnostics = new List<ConnectionDiagnostic>(); transfer.Diagnostic += diagnostics.Add;
            await CheckThrowsAsync<InvalidOperationException>(() => transfer.ListAsync("/", CancellationToken.None, TimeSpan.FromSeconds(1)));
            Check(backend.ListCalls == 0, protocol + " rejects file operations before connection");
            using (var preCancelledConnect = new CancellationTokenSource())
            {
                preCancelledConnect.Cancel();
                await CheckThrowsAsync<OperationCanceledException>(() => transfer.ConnectAsync(preCancelledConnect.Token, TimeSpan.FromSeconds(1)));
                Check(backend.ConnectCalls == 0 && diagnostics.Last().ErrorCode == ConnectionErrorCode.Cancelled,
                    protocol + " pre-cancelled connection emits a diagnostic without opening the adapter");
            }
            await transfer.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1));
            await transfer.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1));
            Check(transfer.State == ConnectionState.Connected && backend.ConnectCalls == 1, protocol + " connects once through the shared async contract");

            byte[] content = Enumerable.Range(0, 300000).Select(i => (byte)(i % 251)).ToArray();
            string local = Path.Combine(root, "contract-upload-" + protocol + ".bin");
            string download = local + ".download"; File.WriteAllBytes(local, content); backend.Files["/data.bin"] = [42];
            var progress = new List<TransferProgress>();
            await transfer.UploadAsync(local, "/data.bin", CancellationToken.None, TimeSpan.FromSeconds(5), new CallbackProgress<TransferProgress>(progress.Add));
            Check(backend.Files["/data.bin"].SequenceEqual(content) && backend.Files.Count == 1 && backend.CommitCalls == 1 && !backend.WriteOpen,
                protocol + " uploads through a closed temporary stream before async commit");
            Check(progress.Count > 1 && progress[^1].BytesTransferred == content.Length && progress.All(p => p.Direction == TransferDirection.Upload && p.TotalBytes == content.Length)
                && progress.Zip(progress.Skip(1)).All(p => p.First.BytesTransferred < p.Second.BytesTransferred), protocol + " reports structured cumulative upload progress");
            await transfer.DownloadAsync("/data.bin", download, CancellationToken.None, TimeSpan.FromSeconds(5));
            Check(File.ReadAllBytes(download).SequenceEqual(content) && backend.OpenReads == 1, protocol + " downloads bytes through async shared transfer policy");

            using var cancelUpload = new CancellationTokenSource();
            await CheckThrowsAsync<OperationCanceledException>(() => transfer.UploadAsync(local, "/data.bin", cancelUpload.Token, TimeSpan.FromSeconds(5),
                new CallbackProgress<TransferProgress>(_ => cancelUpload.Cancel())));
            Check(backend.Files.Count == 1 && backend.Files["/data.bin"].SequenceEqual(content) && backend.CommitCalls == 1,
                protocol + " cancelled upload preserves destination and removes temporary file");
            using var cancelDownload = new CancellationTokenSource(); File.WriteAllText(download, "original");
            await CheckThrowsAsync<OperationCanceledException>(() => transfer.DownloadAsync("/data.bin", download, cancelDownload.Token, TimeSpan.FromSeconds(5),
                new CallbackProgress<TransferProgress>(_ => cancelDownload.Cancel())));
            Check(File.ReadAllText(download) == "original" && !Directory.EnumerateFiles(root, "*.part").Any(), protocol + " cancelled download preserves local target and cleans temporary file");
            Check(diagnostics.Last().ErrorCode == ConnectionErrorCode.Cancelled && diagnostics.Last().OperationId == transfer.OperationId
                && diagnostics.Last().Protocol == protocol, protocol + " cancellation diagnostic preserves operation and protocol identity");

            using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
            int reads = backend.OpenReads, writes = backend.OpenWrites;
            await CheckThrowsAsync<OperationCanceledException>(() => transfer.UploadAsync(local, "/data.bin", preCancelled.Token, TimeSpan.FromSeconds(5)));
            await CheckThrowsAsync<OperationCanceledException>(() => transfer.DownloadAsync("/data.bin", download, preCancelled.Token, TimeSpan.FromSeconds(5)));
            Check(backend.OpenReads == reads && backend.OpenWrites == writes, protocol + " pre-cancelled transfers never open protocol streams");

            backend.CommitError = new IOException("commit failed");
            await CheckThrowsAsync<IOException>(() => transfer.UploadAsync(local, "/data.bin", CancellationToken.None, TimeSpan.FromSeconds(5)));
            Check(backend.Files.Count == 1 && backend.Files["/data.bin"].SequenceEqual(content), protocol + " failed commit cleans temporary file and retains old destination");
            backend.CommitError = null;

            backend.ExistsError = new UnauthorizedAccessException("private endpoint");
            await CheckThrowsAsync<UnauthorizedAccessException>(() => transfer.ExistsAsync("/data.bin", CancellationToken.None, TimeSpan.FromSeconds(1)));
            Check(diagnostics.Last().Stage == ConnectionStage.Reading && diagnostics.Last().ErrorCode == ConnectionErrorCode.PermissionDenied
                && !diagnostics.Last().Message.Contains("private endpoint"), protocol + " existence failure emits a safe reading diagnostic");
            backend.ExistsError = null;

            backend.Files["/existing.bin"] = [99];
            await CheckThrowsAsync<IOException>(() => transfer.RenameAsync("/data.bin", "/existing.bin", CancellationToken.None, TimeSpan.FromSeconds(2)));
            Check(backend.RenameCalls == 0 && backend.Files["/existing.bin"][0] == 99, protocol + " rename refuses overwriting an existing destination");
            await transfer.RenameAsync("/data.bin", "/renamed.bin", CancellationToken.None, TimeSpan.FromSeconds(2));
            await transfer.MkdirAsync("/new-directory", CancellationToken.None, TimeSpan.FromSeconds(2));
            var entry = (await transfer.ListAsync("/", CancellationToken.None, TimeSpan.FromSeconds(2))).Single(e => e.Name == "renamed.bin");
            await transfer.DeleteAsync(entry, CancellationToken.None, TimeSpan.FromSeconds(2));
            Check(!await transfer.ExistsAsync("/renamed.bin", CancellationToken.None, TimeSpan.FromSeconds(2)) && backend.Directories.Contains("/new-directory"),
                protocol + " lists renames creates and deletes through the async contract");

            backend.BlockList = true;
            var listing = transfer.ListAsync("/", CancellationToken.None, TimeSpan.FromSeconds(10));
            await backend.ListStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var dispose = transfer.DisposeAsync().AsTask();
            await CheckThrowsAsync<OperationCanceledException>(() => listing);
            await dispose.WaitAsync(TimeSpan.FromSeconds(2));
            Check(backend.DisposeCalls == 1 && transfer.State == ConnectionState.Closed && backend.ListCancellationObserved,
                protocol + " close cancels and awaits an active request before releasing the adapter");
            await transfer.DisposeAsync();
            Check(backend.DisposeCalls == 1, protocol + " repeated async disposal releases the adapter once");
        }

        var retryBackend = new MemoryRemoteFileSystem { ConnectError = new IOException("connect failed") };
        await using (IFileTransfer retry = new FileConnection(Protocol.SFTP, retryBackend))
        {
            await CheckThrowsAsync<IOException>(() => retry.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1)));
            Check(retryBackend.DisposeCalls == 1 && retry.State == ConnectionState.Closed, "failed connection releases adapter resources");
            retryBackend.ConnectError = null; await retry.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1));
            Check(retry.State == ConnectionState.Connected && retryBackend.ConnectCalls == 2, "connection failure can be explicitly retried");
        }

        var cleanupBackend = new MemoryRemoteFileSystem { CommitError = new IOException("original commit error"), CleanupError = true };
        await using (IFileTransfer cleanup = new FileConnection(Protocol.FTP, cleanupBackend))
        {
            await cleanup.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1));
            string source = Path.Combine(root, "cleanup-source.bin"); File.WriteAllBytes(source, [1, 2, 3]);
            Exception? original = null;
            try { await cleanup.UploadAsync(source, "/data.bin", CancellationToken.None, TimeSpan.FromSeconds(1)); } catch (IOException error) { original = error; }
            Check(ReferenceEquals(original, cleanupBackend.CommitError), "best-effort remote cleanup does not replace the transfer error");
        }

        var timeoutBackend = new MemoryRemoteFileSystem { BlockList = true };
        await using (IFileTransfer timeout = new FileConnection(Protocol.FTP, timeoutBackend))
        {
            var timeoutDiagnostics = new List<ConnectionDiagnostic>(); timeout.Diagnostic += timeoutDiagnostics.Add;
            await timeout.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1));
            await CheckThrowsAsync<TimeoutException>(() => timeout.ListAsync("/", CancellationToken.None, TimeSpan.FromMilliseconds(50)));
            Check(timeoutBackend.ListCancellationObserved && timeoutDiagnostics.Last().ErrorCode == ConnectionErrorCode.Timeout,
                "operation timeout cancels the underlying request and emits a stable timeout diagnostic");
        }

        var connectRaceBackend = new MemoryRemoteFileSystem { BlockConnect = true };
        await using (IFileTransfer connectRace = new FileConnection(Protocol.SFTP, connectRaceBackend))
        {
            var connectDiagnostics = new List<ConnectionDiagnostic>(); connectRace.Diagnostic += connectDiagnostics.Add;
            var firstConnect = connectRace.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(2));
            await connectRaceBackend.ConnectStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await CheckThrowsAsync<TimeoutException>(() => connectRace.ConnectAsync(CancellationToken.None, TimeSpan.FromMilliseconds(50)));
            Check(connectRace.State == ConnectionState.Connecting && connectRaceBackend.DisposeCalls == 0
                && connectDiagnostics.Last().ErrorCode == ConnectionErrorCode.Timeout,
                "a queued connect timeout is diagnosed without disposing the active connection attempt");
            connectRaceBackend.ConnectRelease.TrySetResult();
            await firstConnect;
            Check(connectRace.State == ConnectionState.Connected && connectRaceBackend.ConnectCalls == 1,
                "the original connection attempt completes after a queued timeout");
        }
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

        var blocked = new MemoryRemoteFileSystem { BlockList = true };
        using var closingForm = new Form { Width = 960, Height = 640 };
        var closingView = new FileView(new FileConnection(Protocol.FTP, blocked), "/") { Dock = DockStyle.Fill };
        closingForm.Controls.Add(closingView); closingForm.Show();
        var startupWait = System.Diagnostics.Stopwatch.StartNew();
        while (!blocked.ListStarted.Task.IsCompleted && startupWait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(blocked.ListStarted.Task.IsCompleted, "file view starts an async listing request");
        var closeTask = closingView.CloseAsync();
        var closeWait = System.Diagnostics.Stopwatch.StartNew();
        while (!closeTask.IsCompleted && closeWait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        closeTask.GetAwaiter().GetResult();
        Check(closeTask.IsCompleted && blocked.ListCancellationObserved && blocked.DisposeCalls == 1,
            "closing file view cancels active request and awaits asynchronous cleanup");
        closingForm.Close(); Application.DoEvents();
    }

    static void FileTransferFormCloseUi()
    {
        var backend = new MemoryRemoteFileSystem { BlockList = true };
        using var form = new MainForm(new Store(Path.Combine(Path.GetTempPath(), "PortHarbor-close-ui-" + Guid.NewGuid().ToString("N"))));
        form.Show();
        var tabs = FindControl<TabControl>(form);
        var page = new TabPage("async file close");
        page.Controls.Add(new FileView(new FileConnection(Protocol.SFTP, backend), "/") { Dock = DockStyle.Fill });
        tabs.TabPages.Add(page); Application.DoEvents();
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (!backend.ListStarted.Task.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(backend.ListStarted.Task.IsCompleted, "main window contains an active file request before shutdown");
        form.Close();
        wait.Restart();
        while (!form.IsDisposed && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Check(form.IsDisposed && backend.ListCancellationObserved && backend.DisposeCalls == 1,
            "main window shutdown waits for active file cancellation and async cleanup");
    }

    // In-memory adapter exercises shared policy without servers or vendor clients.
    sealed class MemoryRemoteFileSystem : IRemoteFileSystem
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public readonly HashSet<string> Directories = [];
        public Exception? ConnectError, ExistsError, CommitError;
        public bool CleanupError, WriteOpen, BlockList, ListCancellationObserved, BlockConnect;
        public int ConnectCalls, ListCalls, OpenReads, OpenWrites, CommitCalls, RenameCalls, DisposeCalls;
        public TaskCompletionSource ListStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ConnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ConnectRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ConnectAsync(Action<ConnectionStage> reportStage, CancellationToken token)
        {
            ConnectCalls++; reportStage(ConnectionStage.Authenticating); token.ThrowIfCancellationRequested();
            if (BlockConnect)
            {
                ConnectStarted.TrySetResult();
                await ConnectRelease.Task.WaitAsync(token);
            }
            if (ConnectError != null) throw ConnectError;
        }
        public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token)
        {
            ListCalls++;
            if (BlockList)
            {
                ListStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { ListCancellationObserved = true; throw; }
            }
            token.ThrowIfCancellationRequested();
            return Files.Select(f => new RemoteEntry(f.Key.TrimStart('/'), f.Key, false, f.Value.Length, DateTime.UnixEpoch)).ToList();
        }
        public Task<bool> ExistsAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ExistsError != null) return Task.FromException<bool>(ExistsError);
            return Task.FromResult(Files.ContainsKey(path) || Directories.Contains(path));
        }
        public Task<long?> GetLengthAsync(string path, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<long?>(Files.TryGetValue(path, out var bytes) ? bytes.Length : null); }
        public Task<Stream> OpenReadAsync(string path, CancellationToken token)
        { token.ThrowIfCancellationRequested(); OpenReads++; return Task.FromResult<Stream>(new MemoryStream(Files[path], false)); }
        public Task<Stream> OpenWriteAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); OpenWrites++; WriteOpen = true;
            return Task.FromResult<Stream>(new SavedStream(bytes => { Files[path] = bytes; WriteOpen = false; }));
        }
        public Task CompleteWriteAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task CommitUploadAsync(string temporaryPath, string destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (WriteOpen) return Task.FromException(new InvalidOperationException("commit before stream close"));
            CommitCalls++;
            if (CommitError != null) return Task.FromException(CommitError);
            Files[destination] = Files[temporaryPath]; Files.Remove(temporaryPath); return Task.CompletedTask;
        }
        public Task DeleteFileAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (CleanupError) return Task.FromException(new IOException("cleanup failed"));
            Files.Remove(path); return Task.CompletedTask;
        }
        public Task MkdirAsync(string path, CancellationToken token) { token.ThrowIfCancellationRequested(); Directories.Add(path); return Task.CompletedTask; }
        public Task RenameAsync(string source, string destination, CancellationToken token)
        { token.ThrowIfCancellationRequested(); RenameCalls++; Files[destination] = Files[source]; Files.Remove(source); return Task.CompletedTask; }
        public Task DeleteAsync(RemoteEntry entry, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (entry.Directory) Directories.Remove(entry.Path); else Files.Remove(entry.Path); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }

        sealed class SavedStream(Action<byte[]> save) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing) save(ToArray());
                base.Dispose(disposing);
            }
            public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
