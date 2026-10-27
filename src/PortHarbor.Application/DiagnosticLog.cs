namespace PortHarbor;

public sealed record DiagnosticLogEntry(DateTimeOffset At, Guid OperationId, Protocol Protocol, ConnectionState State, ConnectionStage Stage, ConnectionErrorCode ErrorCode, bool Retryable, long? DurationMs, long? BytesTransferred, long? TotalBytes)
{
    public Guid? SessionId { get; init; }
    public long? TimeoutMs { get; init; }
    public long? BytesSent { get; init; }
    public long? BytesReceived { get; init; }
    public long? CharactersDecoded { get; init; }
    public TransferRecoveryState Recovery { get; init; }
}

public interface IDiagnosticLog
{
    void Write(ConnectionDiagnostic diagnostic, long? durationMs = null, long? bytesTransferred = null, long? totalBytes = null);
}

public static class BackgroundTask
{
    public static void Observe(Task task, Action<Exception>? failure = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        _ = task.ContinueWith(completed =>
        {
            if (completed.Exception is not { } error) return;
            try { failure?.Invoke(error.GetBaseException()); }
            catch { /* A failure observer must not create another unobserved task. */ }
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
