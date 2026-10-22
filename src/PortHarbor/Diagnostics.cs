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
    Closing
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

internal static class DiagnosticClassifier
{
    public static ConnectionErrorCode Classify(Exception error, ConnectionStage stage)
    {
        if (error is OperationCanceledException) return ConnectionErrorCode.Cancelled;
        if (error is ArgumentException or InvalidDataException) return ConnectionErrorCode.InvalidConfiguration;
        if (error is UnauthorizedAccessException) return ConnectionErrorCode.PermissionDenied;
        if (error is TimeoutException) return ConnectionErrorCode.Timeout;
        if (error is System.ComponentModel.Win32Exception) return ConnectionErrorCode.PlatformUnavailable;
        if (error is System.Net.Sockets.SocketException) return ConnectionErrorCode.NetworkUnavailable;
        if (stage == ConnectionStage.HostKeyVerification) return ConnectionErrorCode.HostKeyRejected;
        if (stage == ConnectionStage.Authenticating) return ConnectionErrorCode.AuthenticationFailed;
        if (stage is ConnectionStage.OpeningChannel or ConnectionStage.Reading or ConnectionStage.Writing) return ConnectionErrorCode.ProtocolError;
        return ConnectionErrorCode.Unknown;
    }

    public static bool IsRetryable(ConnectionErrorCode code) => code is
        ConnectionErrorCode.NetworkUnavailable or ConnectionErrorCode.Timeout or ConnectionErrorCode.Unknown;
}
