namespace PortHarbor;

/// <summary>A single terminal lifetime. Run completes only after I/O and transport cleanup.</summary>
public interface ITerminalSession : IDisposable, IAsyncDisposable
{
    Guid OperationId { get; }
    ConnectionState State { get; }
    event Action<ConnectionDiagnostic>? Diagnostic;
    event Action<string>? Output;
    Task Run(int columns, int rows, CancellationToken token = default);
    void Send(byte[] bytes);
    void Resize(int columns, int rows);
    Task CloseAsync();
}

/// <summary>One protocol connection, owned by the terminal use case; bytes are UTF-8 payload.</summary>
public interface ITerminalTransport : IAsyncDisposable
{
    Task ConnectAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token);
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token);
    ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token);
    ValueTask ResizeAsync(int columns, int rows, CancellationToken token);
    /// <summary>Nonblocking, permanent stop request; must unblock I/O and be safe during startup.</summary>
    void Abort();
}
