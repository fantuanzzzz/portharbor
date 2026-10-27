using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void DiagnosticBundleChecks(string root)
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
    }
}
