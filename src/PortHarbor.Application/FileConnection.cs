using System.IO;

namespace PortHarbor;

/// <summary>Shared asynchronous transfer policy and diagnostics, independent of vendor protocol clients.</summary>
public sealed class FileConnection : IFileTransfer
{
    static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    readonly Protocol protocol;
    readonly IRemoteFileSystem remote;
    readonly SemaphoreSlim operationGate = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    readonly object disposeSync = new();
    Task? disposeTask;
    bool disposeRequested;
    bool disposed;
    ConnectionStage connectingStage = ConnectionStage.Connecting;
    readonly Guid? sessionId;
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    long transferred;
    long? totalBytes;
    TransferRecoveryState recovery;
    TimeSpan currentTimeout;

    public Guid OperationId { get; } = Guid.NewGuid();
    public ConnectionState State { get; private set; } = ConnectionState.Created;
    public event Action<ConnectionDiagnostic>? Diagnostic;

    readonly Func<Exception, ConnectionStage, ConnectionErrorCode> classifyError;

    public FileConnection(Protocol protocol, IRemoteFileSystem remote,
        Func<Exception, ConnectionStage, ConnectionErrorCode>? classifyError = null, Guid? sessionId = null)
    {
        if (protocol is not (Protocol.FTP or Protocol.SFTP)) throw new ArgumentException("File transfers require FTP or SFTP.", nameof(protocol));
        this.protocol = protocol;
        this.remote = remote ?? throw new ArgumentNullException(nameof(remote));
        this.classifyError = classifyError ?? ConnectionErrorPolicy.Classify;
        this.sessionId = sessionId;
    }

    public async Task ConnectAsync(CancellationToken token, TimeSpan timeout)
    {
        ThrowIfDisposalRequested();
        if (State == ConnectionState.Connected) return;
        currentTimeout = timeout;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        bool enteredConnectionAttempt = false;
        try
        {
            await TimeoutRunner.RunAsync(timeout, linked.Token, async ct =>
            {
                await operationGate.WaitAsync(ct).ConfigureAwait(false);
                enteredConnectionAttempt = true;
                try
                {
                    ThrowIfDisposalRequested();
                    if (State == ConnectionState.Connected) return true;
                    connectingStage = ConnectionStage.Connecting;
                    try
                    {
                        SetState(ConnectionState.Connecting, connectingStage, ConnectionErrorCode.None, false, "Connecting.");
                        await remote.ConnectAsync(stage =>
                        {
                            connectingStage = stage;
                            SetState(ConnectionState.Connecting, stage, ConnectionErrorCode.None, false, "Connecting.");
                        }, ct).ConfigureAwait(false);
                        SetState(ConnectionState.Connected, ConnectionStage.None, ConnectionErrorCode.None, false, "Connected.");
                    }
                    catch (Exception error)
                    {
                        // Classify the operation deadline before cleanup, while retaining
                        // the gate so a queued request cannot race disposal of this client.
                        Exception failure = error is OperationCanceledException && !linked.IsCancellationRequested && ct.IsCancellationRequested
                            ? new TimeoutException("The file connection exceeded its configured timeout.", error) : error;
                        var code = classifyError(failure, connectingStage);
                        SetState(ConnectionState.Failed, connectingStage, code, ConnectionErrorPolicy.IsRetryable(code), "File connection failed.");
                        try { await remote.DisposeAsync().ConfigureAwait(false); }
                        catch { /* Preserve the connect error; the adapter has been asked to release its owners. */ }
                        SetState(ConnectionState.Closed, ConnectionStage.Closing, code, false, "Closed.");
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                        throw;
                    }
                    return true;
                }
                finally { operationGate.Release(); }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (enteredConnectionAttempt && State == ConnectionState.Connecting)
            {
                var code = classifyError(error, connectingStage);
                SetState(ConnectionState.Failed, connectingStage, code, ConnectionErrorPolicy.IsRetryable(code), "File connection failed.");
                try { await remote.DisposeAsync().ConfigureAwait(false); }
                catch { }
                SetState(ConnectionState.Closed, ConnectionStage.Closing, code, false, "Closed.");
            }
            else if (!enteredConnectionAttempt)
            {
                var code = classifyError(error, connectingStage);
                Diagnostic?.Invoke(new ConnectionDiagnostic(OperationId, protocol, State, connectingStage, code, ConnectionErrorPolicy.IsRetryable(code), "Connection request did not acquire the connection."));
            }
            throw;
        }
    }

    public Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken token, TimeSpan timeout)
        => ExecuteAsync(ConnectionStage.Reading, "Listing failed.", timeout, token, ct => remote.ListAsync(path, ct));

    public Task<bool> ExistsAsync(string path, CancellationToken token, TimeSpan timeout)
        => ExecuteAsync(ConnectionStage.Reading, "Existence check failed.", timeout, token, ct => remote.ExistsAsync(path, ct));

    public Task MkdirAsync(string path, CancellationToken token, TimeSpan timeout)
        => ExecuteAsync(ConnectionStage.Writing, "Create directory failed.", timeout, token, async ct => { await remote.MkdirAsync(path, ct).ConfigureAwait(false); return true; });

    public Task RenameAsync(string source, string dest, CancellationToken token, TimeSpan timeout)
        => ExecuteAsync(ConnectionStage.Writing, "Rename failed.", timeout, token, async ct =>
        {
            if (await remote.ExistsAsync(dest, ct).ConfigureAwait(false)) throw new IOException("目标名称已经存在。");
            await remote.RenameAsync(source, dest, ct).ConfigureAwait(false);
            return true;
        });

    public Task DeleteAsync(RemoteEntry entry, CancellationToken token, TimeSpan timeout)
        => ExecuteAsync(ConnectionStage.Writing, "Delete failed.", timeout, token, async ct => { await remote.DeleteAsync(entry, ct).ConfigureAwait(false); return true; });

    public Task DownloadAsync(string remotePath, string local, CancellationToken token, TimeSpan timeout, IProgress<TransferProgress>? progress = null)
        => ExecuteAsync(ConnectionStage.Reading, "Download failed.", timeout, token, async ct =>
        {
            var total = await remote.GetLengthAsync(remotePath, ct).ConfigureAwait(false);
            string temporaryPath = local + ".portharbor-" + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var source = await remote.OpenReadAsync(remotePath, ct).ConfigureAwait(false))
                    await CopyAsync(source, target, TransferDirection.Download, total, ct, progress).ConfigureAwait(false);
                await remote.CompleteReadAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                File.Move(temporaryPath, local, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); recovery = TransferRecoveryState.LocalStagingRemoved; }
            }
            return true;
        });

    public Task UploadAsync(string local, string remotePath, CancellationToken token, TimeSpan timeout, IProgress<TransferProgress>? progress = null)
        => ExecuteAsync(ConnectionStage.Writing, "Upload failed.", timeout, token, async ct =>
        {
            var sourceInfo = new FileInfo(local);
            if (!sourceInfo.Exists) throw new FileNotFoundException("The local upload source does not exist.", local);
            string temporaryPath = remotePath + ".portharbor-" + Guid.NewGuid().ToString("N") + ".part";
            bool committed = false;
            try
            {
                await using (var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var destination = await remote.OpenWriteAsync(temporaryPath, ct).ConfigureAwait(false))
                    await CopyAsync(source, destination, TransferDirection.Upload, sourceInfo.Length, ct, progress).ConfigureAwait(false);
                await remote.CompleteWriteAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                recovery = TransferRecoveryState.RemoteCommitUnconfirmed;
                await remote.CommitUploadAsync(temporaryPath, remotePath, ct).ConfigureAwait(false);
                committed = true;
                recovery = TransferRecoveryState.None;
            }
            finally
            {
                if (!committed) await TryCleanupRemoteAsync(temporaryPath).ConfigureAwait(false);
            }
            return true;
        });

    async Task CopyAsync(Stream source, Stream destination, TransferDirection direction, long? total, CancellationToken token, IProgress<TransferProgress>? progress)
    {
        totalBytes = total;
        var buffer = new byte[65536];
        long transferred = 0;
        long lastReported = -1;
        var reportClock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) break;
            if (total.HasValue && transferred + count > total.Value)
                throw new IOException("The transfer exceeded its expected byte count.");
            await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            transferred += count;
            this.transferred = transferred;
            if (lastReported < 0 || transferred - lastReported >= 4 * 1024 * 1024 || reportClock.Elapsed >= TimeSpan.FromMilliseconds(100))
            {
                progress?.Report(new TransferProgress(direction, transferred, total));
                lastReported = transferred;
                reportClock.Restart();
            }
        }
        if (total.HasValue && transferred != total.Value)
            throw new EndOfStreamException("The transfer ended before its expected byte count.");
        await destination.FlushAsync(token).ConfigureAwait(false);
        if (transferred != lastReported) progress?.Report(new TransferProgress(direction, transferred, total));
    }

    async Task TryCleanupRemoteAsync(string temporaryPath)
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        bool uncertainCommit = recovery == TransferRecoveryState.RemoteCommitUnconfirmed;
        try
        {
            if (await remote.ExistsAsync(temporaryPath, cleanup.Token).ConfigureAwait(false))
                await remote.DeleteFileAsync(temporaryPath, cleanup.Token).ConfigureAwait(false);
            if (!uncertainCommit) recovery = TransferRecoveryState.RemoteStagingRemoved;
        }
        catch { if (!uncertainCommit) recovery = TransferRecoveryState.RemoteCleanupUnconfirmed; }
    }

    async Task<T> ExecuteAsync<T>(ConnectionStage stage, string message, TimeSpan timeout, CancellationToken token, Func<CancellationToken, Task<T>> action)
    {
        ThrowIfDisposalRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        bool entered = false;
        try
        {
            return await TimeoutRunner.RunAsync(timeout, linked.Token, async ct =>
            {
                await operationGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposalRequested();
                    if (State != ConnectionState.Connected) throw new InvalidOperationException("文件连接尚未建立。");
                    entered = true;
                    currentTimeout = timeout; transferred = 0; totalBytes = null; recovery = TransferRecoveryState.None;
                    var duration = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var result = await action(ct).ConfigureAwait(false);
                        Diagnostic?.Invoke(Enrich(new(OperationId, protocol, State, stage, ConnectionErrorCode.None, false, "File operation completed."), duration.ElapsedMilliseconds));
                        return result;
                    }
                    catch (Exception error)
                    {
                        var code = error is OperationCanceledException && !linked.IsCancellationRequested && ct.IsCancellationRequested
                            ? ConnectionErrorCode.Timeout : classifyError(error, stage);
                        Diagnostic?.Invoke(Enrich(new(OperationId, protocol, State, stage, code, ConnectionErrorPolicy.IsRetryable(code), message), duration.ElapsedMilliseconds));
                        throw;
                    }
                }
                finally { operationGate.Release(); }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var code = classifyError(error, stage);
            if (!entered) Diagnostic?.Invoke(Enrich(new(OperationId, protocol, State, stage, code, ConnectionErrorPolicy.IsRetryable(code), message)));
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposeSync)
        {
            if (disposeTask != null) return new ValueTask(disposeTask);
            disposeRequested = true;
            lifetime.Cancel();
            disposeTask = DisposeCoreAsync();
            return new ValueTask(disposeTask);
        }
    }

    async Task DisposeCoreAsync()
    {
        await operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            SetState(ConnectionState.Closing, ConnectionStage.Closing, ConnectionErrorCode.None, false, "Closing.");
            try { await remote.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error)
            {
                var code = classifyError(error, ConnectionStage.Closing);
                Diagnostic?.Invoke(new ConnectionDiagnostic(OperationId, protocol, ConnectionState.Closing, ConnectionStage.Closing, code, false, "File connection cleanup failed."));
            }
            finally { SetState(ConnectionState.Closed, ConnectionStage.Closing, ConnectionErrorCode.None, false, "Closed."); }
        }
        finally
        {
            operationGate.Release();
            lifetime.Dispose();
        }
    }

    void ThrowIfDisposalRequested()
    {
        if (disposeRequested || disposed) throw new ObjectDisposedException(nameof(FileConnection));
    }

    void SetState(ConnectionState state, ConnectionStage stage, ConnectionErrorCode code, bool retryable, string message)
    {
        State = state;
        Diagnostic?.Invoke(Enrich(new(OperationId, protocol, state, stage, code, retryable, message)));
    }

    ConnectionDiagnostic Enrich(ConnectionDiagnostic d, long? duration = null) => d with
    {
        SessionId = sessionId, DurationMs = duration ?? clock.ElapsedMilliseconds,
        TimeoutMs = (long)currentTimeout.TotalMilliseconds, BytesTransferred = transferred, TotalBytes = totalBytes, Recovery = recovery
    };
}
