using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortHarbor;

public sealed record DiagnosticBundlePreview(string Version, string Runtime, string Platform, IReadOnlyList<DiagnosticLogEntry> Events);

public static class DiagnosticBundleService
{
    public static DiagnosticBundlePreview Preview(string version, string runtime, string platform, IEnumerable<DiagnosticLogEntry> events)
        => new(version, runtime, platform, events.TakeLast(500).ToArray());

    public static string ToJson(DiagnosticBundlePreview preview)
        => JsonSerializer.Serialize(new { schemaVersion = 1, preview.Version, preview.Runtime, preview.Platform, events = preview.Events.TakeLast(500) },
            new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });

    public static void Export(string path, DiagnosticBundlePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, ToJson(preview)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
