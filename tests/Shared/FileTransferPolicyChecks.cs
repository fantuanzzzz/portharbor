using PortHarbor;

namespace PortHarbor.Checks;

internal static class FileTransferPolicyChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> Check)
    {
        Check(typeof(FileConnection).Assembly == typeof(SessionService).Assembly
            && typeof(FileConnection).Assembly.GetReferencedAssemblies().All(a => a.Name is not ("System.Windows.Forms" or "Renci.SshNet" or "FluentFTP" or "PortHarbor.Infrastructure")),
            "transfer policy lives in Application without UI, infrastructure or vendor dependencies");
        foreach (var protocol in new[] { Protocol.FTP, Protocol.SFTP })
        {
            var truncated = new MemoryRemoteFileSystem { ReportedLength = 100 };
            truncated.Files["/truncated.bin"] = [1, 2, 3];
            await using (var downloadCheck = new FileConnection(protocol, truncated))
            {
                await downloadCheck.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1));
                string destination = Path.Combine(root, "truncated-" + protocol + ".bin");
                File.WriteAllText(destination, "existing local content");
                await CheckThrowsAsync<EndOfStreamException>(() => downloadCheck.DownloadAsync("/truncated.bin", destination, CancellationToken.None, TimeSpan.FromSeconds(1)));
                Check(File.ReadAllText(destination) == "existing local content" && !Directory.EnumerateFiles(root, "*.part").Any(),
                    protocol + " premature download EOF preserves the local target and removes its temporary file");
                File.Delete(destination);
                await CheckThrowsAsync<EndOfStreamException>(() => downloadCheck.DownloadAsync("/truncated.bin", destination, CancellationToken.None, TimeSpan.FromSeconds(1)));
                Check(!File.Exists(destination) && !Directory.EnumerateFiles(root, "*.part").Any(),
                    protocol + " premature download EOF never publishes a new incomplete target");
                truncated.ReportedLength = 2;
                File.WriteAllText(destination, "existing local content");
                await CheckThrowsAsync<IOException>(() => downloadCheck.DownloadAsync("/truncated.bin", destination, CancellationToken.None, TimeSpan.FromSeconds(1)));
                Check(File.ReadAllText(destination) == "existing local content" && truncated.CompleteReadCalls == 0,
                    protocol + " unexpected extra download bytes cannot replace the local target");
            }
            var completion = new MemoryRemoteFileSystem { CompleteReadError = new IOException("private completion failure"), UnknownLength = true };
            completion.Files["/data.bin"] = [1, 2, 3];
            await using (var downloadCheck = new FileConnection(protocol, completion))
            {
                await downloadCheck.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(1));
                string destination = Path.Combine(root, "completion-" + protocol + ".bin");
                File.WriteAllText(destination, "previous target");
                Exception? error = null;
                try { await downloadCheck.DownloadAsync("/data.bin", destination, CancellationToken.None, TimeSpan.FromSeconds(1)); }
                catch (IOException observed) { error = observed; }
                Check(ReferenceEquals(error, completion.CompleteReadError) && File.ReadAllText(destination) == "previous target"
                    && !completion.ReadOpen && !Directory.EnumerateFiles(root, "*.part").Any(),
                    protocol + " failed download confirmation preserves the target even when length is unknown");
                completion.CompleteReadError = null;
                await downloadCheck.DownloadAsync("/data.bin", destination, CancellationToken.None, TimeSpan.FromSeconds(1));
                Check(File.ReadAllBytes(destination).SequenceEqual(completion.Files["/data.bin"]) && completion.CompleteReadCalls == 2,
                    protocol + " unknown length downloads commit after closed-stream protocol confirmation");
                File.WriteAllText(destination, "previous target");
                completion.BlockReadCompletion = true;
                var downloadTask = downloadCheck.DownloadAsync("/data.bin", destination, CancellationToken.None, TimeSpan.FromSeconds(5));
                await completion.ReadCompletionBlocked.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var closeTask = downloadCheck.DisposeAsync().AsTask();
                await CheckThrowsAsync<OperationCanceledException>(() => downloadTask);
                await closeTask.WaitAsync(TimeSpan.FromSeconds(2));
                Check(completion.ReadCompletionCancelled && completion.DisposeCalls == 1 && File.ReadAllText(destination) == "previous target"
                    && !Directory.EnumerateFiles(root, "*.part").Any(),
                    protocol + " close cancels pending download confirmation before committing and cleans local staging");
            }
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
            Check(File.ReadAllBytes(download).SequenceEqual(content) && backend.OpenReads == 1 && backend.CompleteReadCalls == 1 && !backend.ReadOpen,
                protocol + " downloads bytes through async shared transfer policy after closed-stream confirmation");

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

    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    static async Task CheckThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action().ConfigureAwait(false); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name + " to be thrown.");
    }

    sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
