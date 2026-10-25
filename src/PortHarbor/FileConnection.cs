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

    public Guid OperationId { get; } = Guid.NewGuid();
    public ConnectionState State { get; private set; } = ConnectionState.Created;
    public event Action<ConnectionDiagnostic>? Diagnostic;

    public FileConnection(Session session, Store store, string password, Func<string, bool> confirm)
        : this(session.Protocol, RemoteFileSystemFactory.Create(session, store, password, confirm)) { }

    internal FileConnection(Protocol protocol, IRemoteFileSystem remote)
    {
        this.protocol = protocol;
        this.remote = remote;
    }

    public async Task ConnectAsync(CancellationToken token, TimeSpan timeout)
    {
        ThrowIfDisposalRequested();
        if (State == ConnectionState.Connected) return;
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
                        var code = DiagnosticClassifier.Classify(error, connectingStage);
                        SetState(ConnectionState.Failed, connectingStage, code, DiagnosticClassifier.IsRetryable(code), "File connection failed.");
                        try { await remote.DisposeAsync().ConfigureAwait(false); }
                        catch { }
                        SetState(ConnectionState.Closed, ConnectionStage.Closing, code, false, "Closed.");
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
                var code = DiagnosticClassifier.Classify(error, connectingStage);
                SetState(ConnectionState.Failed, connectingStage, code, DiagnosticClassifier.IsRetryable(code), "File connection failed.");
                try { await remote.DisposeAsync().ConfigureAwait(false); }
                catch { }
                SetState(ConnectionState.Closed, ConnectionStage.Closing, code, false, "Closed.");
            }
            else if (!enteredConnectionAttempt)
            {
                var code = DiagnosticClassifier.Classify(error, connectingStage);
                Diagnostic?.Invoke(new ConnectionDiagnostic(OperationId, protocol, State, connectingStage, code, DiagnosticClassifier.IsRetryable(code), "Connection request did not acquire the connection."));
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
            await using var source = await remote.OpenReadAsync(remotePath, ct).ConfigureAwait(false);
            await CopyAtomicallyAsync(source, local, TransferDirection.Download, total, ct, progress).ConfigureAwait(false);
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
                await remote.CommitUploadAsync(temporaryPath, remotePath, ct).ConfigureAwait(false);
                committed = true;
            }
            finally
            {
                if (!committed) await TryCleanupRemoteAsync(temporaryPath).ConfigureAwait(false);
            }
            return true;
        });

    static async Task CopyAtomicallyAsync(Stream source, string destination, TransferDirection direction, long? total, CancellationToken token, IProgress<TransferProgress>? progress)
    {
        token.ThrowIfCancellationRequested();
        string temporaryPath = destination + ".portharbor-" + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await CopyAsync(source, target, direction, total, token, progress).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    static async Task CopyAsync(Stream source, Stream destination, TransferDirection direction, long? total, CancellationToken token, IProgress<TransferProgress>? progress)
    {
        var buffer = new byte[65536];
        long transferred = 0;
        long lastReported = -1;
        var reportClock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            transferred += count;
            if (lastReported < 0 || transferred - lastReported >= 4 * 1024 * 1024 || reportClock.Elapsed >= TimeSpan.FromMilliseconds(100))
            {
                progress?.Report(new TransferProgress(direction, transferred, total));
                lastReported = transferred;
                reportClock.Restart();
            }
        }
        await destination.FlushAsync(token).ConfigureAwait(false);
        if (transferred != lastReported) progress?.Report(new TransferProgress(direction, transferred, total));
    }

    async Task TryCleanupRemoteAsync(string temporaryPath)
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        try
        {
            if (await remote.ExistsAsync(temporaryPath, cleanup.Token).ConfigureAwait(false))
                await remote.DeleteFileAsync(temporaryPath, cleanup.Token).ConfigureAwait(false);
        }
        catch { }
    }

    async Task<T> ExecuteAsync<T>(ConnectionStage stage, string message, TimeSpan timeout, CancellationToken token, Func<CancellationToken, Task<T>> action)
    {
        ThrowIfDisposalRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        try
        {
            return await TimeoutRunner.RunAsync(timeout, linked.Token, async ct =>
            {
                await operationGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    ThrowIfDisposalRequested();
                    if (State != ConnectionState.Connected) throw new InvalidOperationException("文件连接尚未建立。");
                    return await action(ct).ConfigureAwait(false);
                }
                finally { operationGate.Release(); }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var code = DiagnosticClassifier.Classify(error, stage);
            Diagnostic?.Invoke(new ConnectionDiagnostic(OperationId, protocol, State, stage, code, DiagnosticClassifier.IsRetryable(code), message));
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
                var code = DiagnosticClassifier.Classify(error, ConnectionStage.Closing);
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
        Diagnostic?.Invoke(new ConnectionDiagnostic(OperationId, protocol, state, stage, code, retryable, message));
    }
}
