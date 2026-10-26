using System.ComponentModel;
using PortHarbor;

namespace PortHarbor.Checks;

internal static class RemoteDesktopChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var platform = new FixtureLauncher();
        var paths = new AppDataPaths(false, "fixture-directory", Path.Combine("fixture-directory", "settings.json"), null, "fixture-temp");
        var launcher = new RdpLauncher(platform, paths);
        var session = new Session { Protocol = Protocol.RDP, Host = "2001:db8::1", Port = 3390, Username = "用户", RdpFullScreen = false, Clipboard = false };
        var events = new List<ConnectionDiagnostic>();
        string config = RdpLauncher.Configuration(session);
        check(config.Contains("full address:s:[2001:db8::1]:3390\r\nusername:s:用户\r\n")
            && config.Contains("screen mode id:i:1\r\n") && config.Contains("redirectclipboard:i:0\r\n")
            && config.Contains("prompt for credentials:i:1\r\n") && !config.Contains("password", StringComparison.OrdinalIgnoreCase),
            "RDP format preserves IPv6, Unicode, display and clipboard settings without storing credentials");
        foreach (var invalid in new[] { session with { Host = "" }, session with { Username = "user\r\nredirectclipboard:i:1" }, session with { Protocol = Protocol.SSH } })
        {
            events.Clear();
            try { launcher.Launch(invalid, events.Add); } catch (ArgumentException) { }
            check(platform.Calls == 0 && events.Last().State == ConnectionState.Failed && events.Last().Stage == ConnectionStage.Validation
                && events.Last().ErrorCode == ConnectionErrorCode.InvalidConfiguration,
                "invalid RDP " + (invalid.Host == "" ? "host" : invalid.Protocol == Protocol.SSH ? "protocol" : "username") + " is rejected before accessing platform resources");
        }
        events.Clear();
        platform.Error = new UnauthorizedAccessException("private path");
        platform.FailureStage = ConnectionStage.Writing;
        try { launcher.Launch(session, events.Add); } catch (UnauthorizedAccessException) { }
        check(events.Last().State == ConnectionState.Failed && events.Last().Stage == ConnectionStage.Writing
            && events.Last().ErrorCode == ConnectionErrorCode.PermissionDenied && !events.Any(e => e.State == ConnectionState.Connected),
            "RDP write failure retains its stage and does not report a successful launch");
        events.Clear();
        platform.Error = new Win32Exception(2, "private executable");
        platform.FailureStage = ConnectionStage.LaunchingClient;
        try { launcher.Launch(session, events.Add); } catch (Win32Exception) { }
        var failure = events.Last();
        check(failure.State == ConnectionState.Failed && failure.Stage == ConnectionStage.LaunchingClient
            && failure.ErrorCode == ConnectionErrorCode.PlatformUnavailable && !failure.Retryable,
            "RDP startup failure is reported as unavailable platform capability");
        check(events.All(e => e.OperationId == failure.OperationId && e.Protocol == Protocol.RDP && e.Message == ""),
            "RDP diagnostics retain operation identity without raw paths or exception messages");
        events.Clear(); platform.Error = new PlatformNotSupportedException("private platform details");
        try { launcher.Launch(session, events.Add); } catch (PlatformNotSupportedException) { }
        check(events.Last().ErrorCode == ConnectionErrorCode.PlatformUnavailable,
            "RDP unsupported platform reports a stable platform-unavailable error");
        events.Clear(); platform.Error = null;
        launcher.Launch(session, events.Add);
        check(platform.Configuration == config && platform.Directory == "fixture-temp"
            && events.Select(e => (e.State, e.Stage)).SequenceEqual(new[]
            {
                (ConnectionState.Connecting, ConnectionStage.Validation), (ConnectionState.Connecting, ConnectionStage.Writing),
                (ConnectionState.Connecting, ConnectionStage.LaunchingClient), (ConnectionState.Connected, ConnectionStage.LaunchingClient)
            }) && events.Last().OperationId != failure.OperationId,
            "RDP retry delegates configuration to its port with a new operation and reports external client startup");
    }

    sealed class FixtureLauncher : IRemoteDesktopLauncher
    {
        internal int Calls;
        internal string? Configuration, Directory;
        internal Exception? Error;
        internal ConnectionStage FailureStage;
        public void Launch(string configuration, string directory, Action<ConnectionStage> reportStage)
        {
            Calls++; Configuration = configuration; Directory = directory;
            reportStage(ConnectionStage.Writing);
            if (Error != null && FailureStage == ConnectionStage.Writing) throw Error;
            reportStage(ConnectionStage.LaunchingClient);
            if (Error != null) throw Error;
        }
    }
}
