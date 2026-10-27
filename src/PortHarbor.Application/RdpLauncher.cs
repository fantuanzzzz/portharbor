namespace PortHarbor;

public sealed class RdpLauncher(IRemoteDesktopLauncher launcher, IAppDataPaths paths)
{
    public static string Configuration(Session s)
    {
        s.Validate();
        if (s.Protocol != Protocol.RDP) throw new ArgumentException("远程桌面启动只接受 RDP 会话。", nameof(s));
        var options = (RdpProtocolConfiguration)s.ProtocolConfiguration;
        if (options.Username.Any(char.IsControl)) throw new ArgumentException("用户名不能包含控制字符。");
        string host = options.Host.Contains(':') ? $"[{options.Host}]" : options.Host;
        return $"full address:s:{host}:{options.Port}\r\nusername:s:{options.Username}\r\nscreen mode id:i:{(options.FullScreen ? 2 : 1)}\r\ndesktopwidth:i:1280\r\ndesktopheight:i:800\r\nredirectclipboard:i:{(options.Clipboard ? 1 : 0)}\r\nprompt for credentials:i:1\r\nauthentication level:i:1\r\nenablecredsspsupport:i:1\r\n";
    }
    public void Launch(Session s, Action<ConnectionDiagnostic>? diagnostic = null)
    {
        var operationId = Guid.NewGuid();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var stage = ConnectionStage.Validation;
        void Report(ConnectionState state, ConnectionErrorCode code = ConnectionErrorCode.None)
            => diagnostic?.Invoke(new(operationId, Protocol.RDP, state, stage, code, ConnectionErrorPolicy.IsRetryable(code), string.Empty) { SessionId = s.Id, DurationMs = elapsed.ElapsedMilliseconds });
        try
        {
            Report(ConnectionState.Connecting);
            string configuration = Configuration(s);
            launcher.Launch(configuration, paths.TemporaryDirectory, value => { stage = value; Report(ConnectionState.Connecting); });
            // Launch success says nothing about authentication in the external client.
            Report(ConnectionState.Connected);
        }
        catch (Exception error)
        {
            Report(ConnectionState.Failed, ConnectionErrorPolicy.Classify(error, stage));
            throw;
        }
    }
}
