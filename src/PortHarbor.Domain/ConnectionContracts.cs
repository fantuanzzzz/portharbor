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
    string Message);
