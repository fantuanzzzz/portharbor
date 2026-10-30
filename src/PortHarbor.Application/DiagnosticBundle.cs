using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortHarbor;

public sealed record DiagnosticBuildIdentity(string? Commit, bool? Dirty, string? SourceSnapshotSha256, string? SdkVersion);

public sealed record DiagnosticBundlePreview(string Version, string Runtime, string Platform, IReadOnlyList<DiagnosticLogEntry> Events,
    DiagnosticBuildIdentity? Build = null);

public static class DiagnosticBundleService
{
    public static DiagnosticBundlePreview Preview(string version, string runtime, string platform, IEnumerable<DiagnosticLogEntry> events,
        DiagnosticBuildIdentity? build = null)
        => new(version, runtime, platform, events.TakeLast(500).ToArray(), SafeBuild(build));

    static DiagnosticBuildIdentity? SafeBuild(DiagnosticBuildIdentity? build) => build == null ? null : new(
        IsHex(build.Commit, 40, 64) ? build.Commit : null, build.Dirty,
        IsHex(build.SourceSnapshotSha256, 64, 64) ? build.SourceSnapshotSha256 : null,
        build.SdkVersion != null && Version.TryParse(build.SdkVersion, out _) ? build.SdkVersion : null);

    static bool IsHex(string? value, int min, int max) => value != null && value.Length >= min && value.Length <= max &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string ToJson(DiagnosticBundlePreview preview)
        => JsonSerializer.Serialize(new { schemaVersion = 2, preview.Version, preview.Runtime, preview.Platform, Build = SafeBuild(preview.Build), events = preview.Events.TakeLast(500) },
            new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } });

    public static void Export(string path, DiagnosticBundlePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, ToJson(preview)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
