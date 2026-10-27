namespace PortHarbor;

public static class DiagnosticClassifier
{
    public static ConnectionErrorCode Classify(Exception error, ConnectionStage stage)
    {
        if (error is HostKeyRejectedException) return ConnectionErrorCode.HostKeyRejected;
        if (error is Renci.SshNet.Common.SshAuthenticationException) return ConnectionErrorCode.AuthenticationFailed;
        if (error is FluentFTP.Exceptions.FtpAuthenticationException) return ConnectionErrorCode.AuthenticationFailed;
        if (error is Renci.SshNet.Common.SshOperationTimeoutException) return ConnectionErrorCode.Timeout;
        if (error is OperationCanceledException) return ConnectionErrorCode.Cancelled;
        if (error is ArgumentException or InvalidDataException) return ConnectionErrorCode.InvalidConfiguration;
        if (error is UnauthorizedAccessException) return ConnectionErrorCode.PermissionDenied;
        if (error is TimeoutException) return ConnectionErrorCode.Timeout;
        if (error is System.Net.Sockets.SocketException) return ConnectionErrorCode.NetworkUnavailable;
        if (error is System.ComponentModel.Win32Exception) return ConnectionErrorCode.PlatformUnavailable;
        if (error.InnerException != null)
        {
            var innerCode = Classify(error.InnerException, ConnectionStage.None);
            if (innerCode != ConnectionErrorCode.Unknown) return innerCode;
        }
        if (error is Renci.SshNet.Common.SshConnectionException { DisconnectReason: Renci.SshNet.Messages.Transport.DisconnectReason.ConnectionLost })
            return ConnectionErrorCode.NetworkUnavailable;
        if (error is Renci.SshNet.Common.SshException) return ConnectionErrorCode.ProtocolError;
        if (stage == ConnectionStage.HostKeyVerification) return ConnectionErrorCode.HostKeyRejected;
        if (stage == ConnectionStage.Authenticating) return ConnectionErrorCode.AuthenticationFailed;
        if (stage is ConnectionStage.OpeningChannel or ConnectionStage.Reading or ConnectionStage.Writing) return ConnectionErrorCode.ProtocolError;
        return ConnectionErrorCode.Unknown;
    }

    public static bool IsRetryable(ConnectionErrorCode code) => code is
        ConnectionErrorCode.NetworkUnavailable or ConnectionErrorCode.Timeout or ConnectionErrorCode.Unknown;
}
