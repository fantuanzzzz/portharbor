namespace PortHarbor;

public sealed class RdpLauncher(IRemoteDesktopLauncher launcher, IAppDataPaths paths)
{
    public static string Configuration(Session s)
    {
        s.Validate();
        if (s.Protocol != Protocol.RDP) throw new ArgumentException("远程桌面启动只接受 RDP 会话。", nameof(s));
        if (s.Username.Any(char.IsControl)) throw new ArgumentException("用户名不能包含控制字符。");
        string host = s.Host.Contains(':') ? $"[{s.Host}]" : s.Host;
        return $"full address:s:{host}:{s.Port}\r\nusername:s:{s.Username}\r\nscreen mode id:i:{(s.RdpFullScreen ? 2 : 1)}\r\ndesktopwidth:i:1280\r\ndesktopheight:i:800\r\nredirectclipboard:i:{(s.Clipboard ? 1 : 0)}\r\nprompt for credentials:i:1\r\nauthentication level:i:1\r\nenablecredsspsupport:i:1\r\n";
    }
    public void Launch(Session s, Action<ConnectionDiagnostic>? diagnostic = null)
    {
        var operationId = Guid.NewGuid();
        var stage = ConnectionStage.Validation;
        void Report(ConnectionState state, ConnectionErrorCode code = ConnectionErrorCode.None)
            => diagnostic?.Invoke(new(operationId, Protocol.RDP, state, stage, code, ConnectionErrorPolicy.IsRetryable(code), string.Empty));
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
