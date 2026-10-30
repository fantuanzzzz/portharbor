using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Channels;

namespace PortHarbor;

/// <summary>Terminal lifecycle, ordered input and decoding, independent of UI and protocol vendors.</summary>
public sealed class TerminalConnection : ITerminalSession
{
    readonly Session session;
    readonly ITerminalTransport transport;
    readonly Func<Exception, ConnectionStage, ConnectionErrorCode> classifyError;
    readonly TimeSpan connectTimeout;
    readonly TimeSpan writeTimeout;
    readonly System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
    long received, sent, decodedCharacters;
    readonly CancellationTokenSource stop = new();
    readonly object gate = new();
    readonly Channel<Input> writes = Channel.CreateBounded<Input>(new BoundedChannelOptions(256)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    sealed record Input(byte[]? Bytes, int Columns = 0, int Rows = 0);
    Task? runTask, closeTask;
    bool closeRequested;
    int stopRequested, state = (int)ConnectionState.Created;
    public Guid OperationId { get; } = Guid.NewGuid();
    public ConnectionState State => (ConnectionState)Volatile.Read(ref state);
    public event Action<ConnectionDiagnostic>? Diagnostic;
    public event Action<string>? Output;

    public TerminalConnection(Session session, ITerminalTransport transport,
        Func<Exception, ConnectionStage, ConnectionErrorCode>? classifyError = null,
        TimeSpan? connectTimeout = null, TimeSpan? writeTimeout = null)
    {
        this.session = session with { };
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.classifyError = classifyError ?? ConnectionErrorPolicy.Classify;
        this.connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(15);
        this.writeTimeout = writeTimeout ?? TimeSpan.FromSeconds(15);
        if (this.connectTimeout <= TimeSpan.Zero || this.writeTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(connectTimeout));
    }

    public Task Run(int columns, int rows, CancellationToken token = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closeRequested, this);
            if (runTask != null) throw new InvalidOperationException("A terminal session can only run once.");
            return runTask = Task.Run(() => RunCoreAsync(columns, rows, token));
        }
    }

    async Task RunCoreAsync(int columns, int rows, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        using var cancellation = linked.Token.Register(RequestStop);
        Task writer = Task.CompletedTask;
        ExceptionDispatchInfo? failure = null, writeFailure = null;
        ConnectionStage stage = ConnectionStage.Validation;
        try
        {
            SetState(ConnectionState.Connecting, stage);
            linked.Token.ThrowIfCancellationRequested();
            session.Validate();
            if (session.Protocol is not (Protocol.SSH or Protocol.Telnet or Protocol.Serial or Protocol.Shell))
                throw new ArgumentException("The selected protocol is not a terminal protocol.");
            if (columns <= 0 || rows <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            await TimeoutRunner.RunAsync(connectTimeout, linked.Token, async ct =>
            {
                try
                {
                    await transport.ConnectAsync(columns, rows, current =>
                    {
                        if (!ct.IsCancellationRequested) { stage = current; SetState(ConnectionState.Connecting, current); }
                    }, ct).ConfigureAwait(false);
                }
                catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
            }).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            SetState(ConnectionState.Connected, ConnectionStage.None);
            writer = WriteLoopAsync(linked.Token, error =>
            {
                writeFailure = ExceptionDispatchInfo.Capture(error);
                ReportFailure(error, ConnectionStage.Writing);
                RequestStop();
            });
            Output?.Invoke("\x1b[32m[已连接；Ctrl+Shift+C 复制，Ctrl+Shift+V 粘贴]\x1b[0m\r\n");
            var buffer = new byte[16384];
            var decoder = Encoding.UTF8.GetDecoder();
            var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
            stage = ConnectionStage.Reading;
            while (true)
            {
                int count = await transport.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                if (count == 0) break;
                linked.Token.ThrowIfCancellationRequested();
                int decoded = decoder.GetChars(buffer, 0, count, chars, 0);
                Interlocked.Add(ref received, count);
                Interlocked.Add(ref decodedCharacters, decoded);
                if (decoded > 0) Output?.Invoke(new string(chars, 0, decoded));
            }
        }
        catch (SshAuthenticationCancelledException)
        {
            Publish(ConnectionState.Closed, ConnectionStage.Closing, ConnectionErrorCode.Cancelled, false, "Connection cancelled.");
        }
        catch (Exception) when (linked.IsCancellationRequested)
        {
            if (writeFailure == null) Publish(ConnectionState.Closed, ConnectionStage.Closing, ConnectionErrorCode.Cancelled, false, "Connection cancelled.");
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
            ReportFailure(error, stage);
        }
        finally
        {
            SetState(ConnectionState.Closing, ConnectionStage.Closing);
            RequestStop();
            await writer.ConfigureAwait(false);
            try { await transport.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error)
            {
                if (failure == null && writeFailure == null)
                {
                    failure = ExceptionDispatchInfo.Capture(error);
                    ReportFailure(error, ConnectionStage.Closing);
                }
            }
            SetState(ConnectionState.Closed, ConnectionStage.Closing);
            Output?.Invoke("\r\n\x1b[90m[连接已关闭]\x1b[0m\r\n");
        }
        (failure ?? writeFailure)?.Throw();
    }

    async Task WriteLoopAsync(CancellationToken token, Action<Exception> failed)
    {
        try
        {
            await foreach (var input in writes.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                await TimeoutRunner.RunAsync(writeTimeout, token, async ct =>
                {
                    // A worker around a synchronous vendor API must be aborted when
                    // its deadline expires, then awaited before releasing owners.
                    using var interruption = ct.Register(transport.Abort);
                    try
                    {
                        if (input.Bytes != null)
                        {
                            await transport.WriteAsync(input.Bytes, ct).ConfigureAwait(false);
                            Interlocked.Add(ref sent, input.Bytes.Length);
                        }
                        else await transport.ResizeAsync(input.Columns, input.Rows, ct).ConfigureAwait(false);
                    }
                    catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
                }).ConfigureAwait(false);
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception error) { failed(error); }
    }

    public void Send(byte[] bytes)
    {
        if (Volatile.Read(ref stopRequested) != 0) return;
        if (!writes.Writer.TryWrite(new(bytes.ToArray()))) Output?.Invoke("\r\n[输入队列已满，请稍后重试]\r\n");
    }

    public void Resize(int columns, int rows)
    {
        if (columns > 0 && rows > 0 && State == ConnectionState.Connected && Volatile.Read(ref stopRequested) == 0)
            writes.Writer.TryWrite(new(null, columns, rows));
    }

    void RequestStop()
    {
        if (Interlocked.Exchange(ref stopRequested, 1) != 0) return;
        writes.Writer.TryComplete();
        stop.Cancel();
        transport.Abort();
    }

    public Task CloseAsync()
    {
        lock (gate)
        {
            if (closeTask != null) return closeTask;
            closeRequested = true;
            RequestStop();
            return closeTask = Task.Run(async () =>
            {
                if (runTask != null)
                {
                    // Run already publishes failures; closing must still finish cleanup.
                    try { await runTask.ConfigureAwait(false); } catch { }
                }
                else
                {
                    SetState(ConnectionState.Closing, ConnectionStage.Closing);
                    try { await transport.DisposeAsync().ConfigureAwait(false); }
                    finally { SetState(ConnectionState.Closed, ConnectionStage.Closing); }
                }
            });
        }
    }

    public void Dispose() => BackgroundTask.Observe(CloseAsync());
    public ValueTask DisposeAsync() => new(CloseAsync());
    void SetState(ConnectionState current, ConnectionStage stage)
    {
        Volatile.Write(ref state, (int)current);
        Publish(current, stage, ConnectionErrorCode.None, false, string.Empty);
    }
    void ReportFailure(Exception error, ConnectionStage stage)
    {
        var code = classifyError(error, stage);
        Publish(ConnectionState.Failed, stage, code, ConnectionErrorPolicy.IsRetryable(code), "Terminal connection failed.");
    }
    void Publish(ConnectionState current, ConnectionStage stage, ConnectionErrorCode code, bool retryable, string message)
        => Diagnostic?.Invoke(new(OperationId, session.Protocol, current, stage, code, retryable, message)
        {
            SessionId = session.Id, DurationMs = elapsed.ElapsedMilliseconds,
            TimeoutMs = (long)(stage == ConnectionStage.Writing ? writeTimeout : connectTimeout).TotalMilliseconds,
            BytesSent = Interlocked.Read(ref sent), BytesReceived = Interlocked.Read(ref received),
            CharactersDecoded = Interlocked.Read(ref decodedCharacters)
        });
}
