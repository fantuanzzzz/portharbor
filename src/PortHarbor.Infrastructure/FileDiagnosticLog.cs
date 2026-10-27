using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortHarbor;

public sealed record DiagnosticLogSettings(bool Enabled = true, long MaxBytes = 1024 * 1024, int RetentionDays = 14, int MaxArchives = 8)
{
    public void Validate()
    {
        if (MaxBytes < 256 || MaxBytes > 16 * 1024 * 1024 || RetentionDays is < 0 or > 365 || MaxArchives is < 0 or > 32)
            throw new ArgumentException("诊断日志大小、保留天数或归档数超出允许范围。");
    }
}

/// <summary>Logs only allowlisted scalar fields. Log failures never fail a connection.</summary>
public sealed class FileDiagnosticLog : IDiagnosticLog, IDisposable
{
    static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    readonly object gate = new();
    readonly string directory;
    readonly List<DiagnosticLogEntry> recent = [];
    DiagnosticLogSettings settings;
    public bool WriteFailed { get; private set; }

    public FileDiagnosticLog(string directory, long maxBytes = 1024 * 1024, int retentionDays = 14)
    {
        this.directory = Path.GetFullPath(directory);
        settings = new(MaxBytes: maxBytes, RetentionDays: retentionDays);
        settings.Validate();
    }

    public DiagnosticLogSettings Settings { get { lock (gate) return settings; } }

    public void Configure(DiagnosticLogSettings value)
    {
        value.Validate();
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json"), temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, path, true);
            settings = value;
        }
    }

    public void LoadSettings()
    {
        lock (gate)
        {
            string path = Path.Combine(directory, "settings.json");
            if (!File.Exists(path)) return;
            try
            {
                var value = JsonSerializer.Deserialize<DiagnosticLogSettings>(File.ReadAllText(path), Json)
                    ?? throw new InvalidDataException("Empty diagnostic settings.");
                value.Validate(); settings = value;
            }
            catch (Exception e) when (IsLogFailure(e)) { WriteFailed = true; }
        }
    }

    public void Write(ConnectionDiagnostic d, long? durationMs = null, long? bytesTransferred = null, long? totalBytes = null)
    {
        var entry = new DiagnosticLogEntry(DateTimeOffset.UtcNow, d.OperationId, d.Protocol, d.State, d.Stage, d.ErrorCode,
            d.Retryable, durationMs ?? d.DurationMs, bytesTransferred ?? d.BytesTransferred, totalBytes ?? d.TotalBytes)
        {
            SessionId = d.SessionId, TimeoutMs = d.TimeoutMs, BytesSent = d.BytesSent,
            BytesReceived = d.BytesReceived, CharactersDecoded = d.CharactersDecoded, Recovery = d.Recovery
        };
        lock (gate)
        {
            if (!settings.Enabled) return;
            recent.Add(entry);
            if (recent.Count > 500) recent.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "diagnostics.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length >= settings.MaxBytes)
                    File.Move(path, Path.Combine(directory, "diagnostics-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".jsonl"));
                File.AppendAllText(path, JsonSerializer.Serialize(entry, Json) + Environment.NewLine);
                var archives = new DirectoryInfo(directory).EnumerateFiles("diagnostics-*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
                for (int i = 0; i < archives.Length; i++)
                    if (i >= settings.MaxArchives || DateTime.UtcNow - archives[i].LastWriteTimeUtc > TimeSpan.FromDays(settings.RetentionDays)) archives[i].Delete();
                WriteFailed = false;
            }
            catch (Exception e) when (IsLogFailure(e)) { WriteFailed = true; }
        }
    }

    // Rebuild scalar DTOs; never copy raw lines, settings, or other local files into a bundle.
    public IReadOnlyList<DiagnosticLogEntry> Snapshot()
    {
        lock (gate)
        {
            var entries = new List<DiagnosticLogEntry>();
            try
            {
                if (Directory.Exists(directory))
                    foreach (var file in new DirectoryInfo(directory).EnumerateFiles("diagnostics*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Take(settings.MaxArchives + 1).Reverse())
                        foreach (string line in File.ReadLines(file.FullName))
                        {
                            try { if (JsonSerializer.Deserialize<DiagnosticLogEntry>(line, Json) is { } entry) entries.Add(entry); }
                            catch (JsonException) { /* Ignore a partial line after a crash. */ }
                            if (entries.Count > 500) entries.RemoveAt(0);
                        }
            }
            catch (Exception e) when (IsLogFailure(e)) { WriteFailed = true; }
            return entries.Concat(recent).Distinct().OrderBy(e => e.At).TakeLast(500).ToArray();
        }
    }

    static bool IsLogFailure(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException or ArgumentException;
    public void Dispose() { /* Each write owns its file handle; there is no background writer. */ }
}
