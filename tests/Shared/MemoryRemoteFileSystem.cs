using PortHarbor;

namespace PortHarbor.Checks;

// In-memory adapter exercises shared policy without servers or vendor clients.
internal sealed class MemoryRemoteFileSystem : IRemoteFileSystem
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
