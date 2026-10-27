using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static async Task DiagnosticLogChecks(string root)
    {
        string directory = Path.Combine(root, "diagnostic-log");
        using var log = new FileDiagnosticLog(directory, maxBytes: 300, retentionDays: 14);
        var id = Guid.NewGuid();
        log.Write(new(id, Protocol.SFTP, ConnectionState.Failed, ConnectionStage.Writing, ConnectionErrorCode.ProtocolError, false, "secret-path password"), 42, 7, 100);
        Check(File.Exists(Path.Combine(directory, "diagnostics.jsonl")), "diagnostic log writes a local JSONL record");
        string text = File.ReadAllText(Path.Combine(directory, "diagnostics.jsonl"));
        Check(text.Contains(id.ToString()) && text.Contains("ProtocolError") && !text.Contains("secret-path") && !text.Contains("password"),
            "diagnostic log stores stable fields without raw message or sensitive context");
        log.Write(new(id, Protocol.FTP, ConnectionState.Closed, ConnectionStage.Closing, ConnectionErrorCode.None, false, ""), 10);
        Check(Directory.EnumerateFiles(directory, "diagnostics-*.jsonl").Any(), "diagnostic log rotates records after the configured size");
        Check(log.Snapshot().Any(e => e.OperationId == id && e.DurationMs == 42 && e.BytesTransferred == 7 && e.TotalBytes == 100),
            "diagnostic snapshot preserves operation identity, duration and file byte counters across rotation");
        log.Configure(new(Enabled: false, MaxBytes: 1024, RetentionDays: 3, MaxArchives: 2));
        string before = File.ReadAllText(Path.Combine(directory, "diagnostics.jsonl"));
        log.Write(new(Guid.NewGuid(), Protocol.SSH, ConnectionState.Closed, ConnectionStage.Closing, ConnectionErrorCode.None, false, ""));
        using var restarted = new FileDiagnosticLog(directory); restarted.LoadSettings();
        Check(!restarted.Settings.Enabled && restarted.Settings.RetentionDays == 3 && restarted.Settings.MaxArchives == 2
            && before == File.ReadAllText(Path.Combine(directory, "diagnostics.jsonl")),
            "diagnostic settings persist across restart and disabling logging stops writes");
        restarted.Configure(new(MaxBytes: 256, RetentionDays: 14, MaxArchives: 1));
        for (int i = 0; i < 5; i++) restarted.Write(new(id, Protocol.SFTP, ConnectionState.Connected, ConnectionStage.Reading, ConnectionErrorCode.None, false, ""));
        Check(Directory.GetFiles(directory, "diagnostics-*.jsonl").Length <= 1, "diagnostic archive count is bounded even during rapid rotation");
        string blocker = Path.Combine(root, "diagnostics-unwritable"); File.WriteAllText(blocker, "occupied");
        using var unavailable = new FileDiagnosticLog(blocker);
        unavailable.Write(new(id, Protocol.SFTP, ConnectionState.Connected, ConnectionStage.Reading, ConnectionErrorCode.None, false, ""));
        Check(unavailable.WriteFailed && unavailable.Snapshot().Any(e => e.OperationId == id),
            "a failed diagnostic write is contained and keeps a bounded in-memory snapshot without breaking connections");
        var failed = new TaskCompletionSource<bool>();
        BackgroundTask.Observe(Task.FromException(new InvalidOperationException("background failure")), _ => failed.TrySetResult(true));
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(failed.Task.IsCompletedSuccessfully, "background task observer consumes unhandled worker faults");
        BackgroundTask.Observe(Task.FromException(new IOException()), _ => throw new IOException("failed observer"));
    }
}
