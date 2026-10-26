namespace PortHarbor;

/// <summary>Owns stream resources across concurrent startup and permanent close requests.</summary>
public abstract class StreamTerminalTransport : ITerminalTransport
{
    readonly object gate = new();
    readonly List<IDisposable> resources = [];
    readonly CancellationTokenSource stopped = new();
    readonly SemaphoreSlim writeGate = new(1, 1);
    bool aborted;
    Task? opening, closing, disposal;
    protected Stream Input { get; set; } = Stream.Null;
    protected Stream Output { get; set; } = Stream.Null;

    protected T Own<T>(T resource) where T : IDisposable
    {
        lock (gate)
        {
            if (!aborted) { resources.Add(resource); return resource; }
        }
        resource.Dispose();
        throw new OperationCanceledException(stopped.Token);
    }

    public Task ConnectAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(aborted, this);
            if (opening != null) throw new InvalidOperationException("Transport startup has already begun.");
            return opening = Task.Run(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopped.Token);
                using var cancellation = linked.Token.Register(Abort);
                linked.Token.ThrowIfCancellationRequested();
                await OpenAsync(columns, rows, reportStage, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
            });
        }
    }

    protected abstract Task OpenAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token);

    // ShellStream and synchronous Windows pipe handles need a worker; Abort closes their owner to unblock I/O.
    public virtual async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
        => await Task.Run(() => Output.Read(buffer.Span), token).ConfigureAwait(false);

    public virtual ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) => WriteRawAsync(bytes, token);
    protected async ValueTask WriteRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        await writeGate.WaitAsync(token).ConfigureAwait(false);
        try { await WriteStreamAsync(bytes, token).ConfigureAwait(false); }
        finally { writeGate.Release(); }
    }
    protected virtual async ValueTask WriteStreamAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
        => await Task.Run(() => { Input.Write(bytes.Span); Input.Flush(); }, token).ConfigureAwait(false);
    public virtual ValueTask ResizeAsync(int columns, int rows, CancellationToken token) => ValueTask.CompletedTask;

    public void Abort()
    {
        lock (gate)
        {
            if (aborted) return;
            aborted = true;
            var owned = resources.AsEnumerable().Reverse().ToArray();
            resources.Clear();
            closing = Task.Run(() =>
            {
                foreach (var resource in owned) { try { resource.Dispose(); } catch { /* Continue releasing remaining owners. */ } }
            });
        }
        stopped.Cancel();
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            return new(disposal ??= Task.Run(async () =>
            {
                Abort();
                if (opening != null) { try { await opening.ConfigureAwait(false); } catch { /* Startup failure belongs to its caller. */ } }
                if (closing != null) await closing.ConfigureAwait(false);
            }));
        }
    }
}
