namespace PortHarbor;

public enum ConnectionState { Created, Connecting, Connected, Closing, Closed, Failed }

public enum ConnectionStage
{
    None,
    Validation,
    Connecting,
    Authenticating,
    HostKeyVerification,
    OpeningChannel,
    Reading,
    Writing,
    Closing,
    LaunchingClient
}

public enum ConnectionErrorCode
{
    None,
    Cancelled,
    InvalidConfiguration,
    AuthenticationFailed,
    HostKeyRejected,
    NetworkUnavailable,
    Timeout,
    PermissionDenied,
    PlatformUnavailable,
    ProtocolError,
    Unknown
}

public sealed record ConnectionDiagnostic(
    Guid OperationId,
    Protocol Protocol,
    ConnectionState State,
    ConnectionStage Stage,
    ConnectionErrorCode ErrorCode,
    bool Retryable,
    string Message)
{
    public Guid? SessionId { get; init; }
    public long? DurationMs { get; init; }
    public long? TimeoutMs { get; init; }
    public long? BytesTransferred { get; init; }
    public long? TotalBytes { get; init; }
    public long? BytesSent { get; init; }
    public long? BytesReceived { get; init; }
    public long? CharactersDecoded { get; init; }
    public TransferRecoveryState Recovery { get; init; }
}

public enum TransferRecoveryState { None, LocalStagingRemoved, RemoteCleanupUnconfirmed, RemoteStagingRemoved, RemoteCommitUnconfirmed }
