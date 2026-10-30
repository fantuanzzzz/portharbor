using PortHarbor;

namespace PortHarbor.Checks;

internal static class DiagnosticExportChecks
{
    internal static void Run(string root, Action<bool, string> Check)
    {
        string path = Path.Combine(root, "diagnostic-bundle.json");
        var id = Guid.NewGuid();
        var preview = DiagnosticBundleService.Preview("0.1.0", Environment.Version.ToString(), "windows", [
            new DiagnosticLogEntry(DateTimeOffset.UtcNow, id, Protocol.SFTP, ConnectionState.Failed, ConnectionStage.Writing, ConnectionErrorCode.ProtocolError, false, 12, 5, 9)]);
        DiagnosticBundleService.Export(path, preview);
        string text = File.ReadAllText(path);
        Check(text == DiagnosticBundleService.ToJson(preview), "diagnostic export matches the exact frozen preview");
        Check(text.Contains("schemaVersion") && text.Contains(id.ToString()) && text.Contains("ProtocolError")
            && !text.Contains("password") && !text.Contains("127.0.0.1") && !text.Contains("secret"),
            "diagnostic bundle preview and export retain safe fields without secrets or endpoint details");
        var bounded = DiagnosticBundleService.Preview("0.1.0", "net8", "windows", Enumerable.Repeat(preview.Events[0], 501));
        Check(bounded.Events.Count == 500, "diagnostic preview bounds the event count before rendering or exporting");
        var build = new DiagnosticBuildIdentity(new string('a', 40), true, new string('b', 64), "8.0.426");
        var identified = DiagnosticBundleService.Preview("0.1.0", "net8", "windows", [], build);
        Check(DiagnosticBundleService.ToJson(identified).Contains(build.Commit!) && identified.Build == build,
            "diagnostic export identifies the exact build commit, dirty source snapshot and SDK");
        var unsafeBuild = new DiagnosticBuildIdentity("secret endpoint", false, "C:\\private", "password");
        var safe = DiagnosticBundleService.Preview("0.1.0", "net8", "windows", [], unsafeBuild);
        string sanitized = DiagnosticBundleService.ToJson(safe with { Build = unsafeBuild });
        Check(safe.Build is { Commit: null, SourceSnapshotSha256: null, SdkVersion: null }
            && !sanitized.Contains("secret") && !sanitized.Contains("private") && !sanitized.Contains("password"),
            "build identity rejects arbitrary strings both at preview and export boundaries");
    }
}
