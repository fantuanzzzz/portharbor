using System.Collections.Concurrent;
using System.Threading.Channels;
using PortHarbor;

namespace PortHarbor.Checks;

internal sealed class ScriptedTerminalTransport : ITerminalTransport
{
    public readonly TaskCompletionSource ConnectStarted = NewSignal(), ConnectRelease = NewSignal(), ConnectCancelled = NewSignal();
    public readonly TaskCompletionSource ReadStarted = NewSignal(), WriteStarted = NewSignal(), WriteRelease = NewSignal();
    public readonly TaskCompletionSource DisposeStarted = NewSignal(), DisposeRelease = NewSignal(), Aborted = NewSignal();
    public readonly Channel<byte[]> Incoming = Channel.CreateUnbounded<byte[]>();
    public readonly ConcurrentQueue<byte[]> Written = new();
    public readonly ConcurrentQueue<string> Actions = new();
    public bool BlockConnect, IgnoreConnectCancellation, BlockWrite, BlockDispose;
    public Exception? ConnectError, ReadError, WriteError;
    public int ConnectCalls, DisposeCalls, ActiveWrites;
    public bool DisposedDuringIo;
    public static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task ConnectAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token)
    {
        Interlocked.Increment(ref ConnectCalls);
        reportStage(ConnectionStage.Authenticating);
        using var registration = token.Register(() => ConnectCancelled.TrySetResult());
        ConnectStarted.TrySetResult();
        if (BlockConnect)
        {
            if (IgnoreConnectCancellation) await ConnectRelease.Task;
            else await ConnectRelease.Task.WaitAsync(token);
        }
        if (ConnectError != null) throw ConnectError;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
    {
        ReadStarted.TrySetResult();
        if (ReadError != null) throw ReadError;
        try
        {
            byte[] bytes = await Incoming.Reader.ReadAsync(token);
            bytes.CopyTo(buffer); return bytes.Length;
        }
        catch (ChannelClosedException) { return 0; }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        Interlocked.Increment(ref ActiveWrites);
        try
        {
            WriteStarted.TrySetResult();
            if (BlockWrite) await WriteRelease.Task;
            token.ThrowIfCancellationRequested();
            if (WriteError != null) throw WriteError;
            Written.Enqueue(bytes.ToArray()); Actions.Enqueue("write:" + Convert.ToHexString(bytes.Span));
        }
        finally { Interlocked.Decrement(ref ActiveWrites); }
    }
    public ValueTask ResizeAsync(int columns, int rows, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Actions.Enqueue($"resize:{columns}x{rows}");
        return ValueTask.CompletedTask;
    }
    public void Abort() => Aborted.TrySetResult();
    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref DisposeCalls);
        DisposedDuringIo = ActiveWrites != 0;
        DisposeStarted.TrySetResult();
        if (BlockDispose) await DisposeRelease.Task;
    }
}
