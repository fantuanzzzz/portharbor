using System.Collections.Concurrent;
using System.Diagnostics;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static async Task SftpCommitFailures(string root, string configPath)
    {
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        string serverDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "sftp", "faults");
        Directory.CreateDirectory(serverDirectory);
        byte[] previous = "previous version"u8.ToArray(), replacement = "complete replacement"u8.ToArray();
        // Missing target after an interrupted rename is recoverable only through
        // an explicit new connection; a lost acknowledgement is never replayed.
        var scenarios = new[]
        {
            (Mode: "stall-restore", Target: "missing", Backup: true, Part: true, Marker: "restore-stalled"),
            (Mode: "reject-publish", Target: "old", Backup: false, Part: false, Marker: "restored"),
            (Mode: "reject-restore", Target: "missing", Backup: true, Part: false, Marker: "restore-rejected"),
            (Mode: "drop-before-backup", Target: "old", Backup: false, Part: true, Marker: "disconnected"),
            (Mode: "drop-after-backup", Target: "missing", Backup: true, Part: true, Marker: "disconnected"),
            (Mode: "drop-before-publish", Target: "missing", Backup: true, Part: true, Marker: "disconnected"),
            (Mode: "drop-after-publish", Target: "new", Backup: true, Part: false, Marker: "published"),
            (Mode: "drop-after-restore", Target: "old", Backup: false, Part: true, Marker: "restored"),
            (Mode: "stall-cleanup", Target: "new", Backup: true, Part: false, Marker: "cleanup-stalled")
        };
        foreach (var scenario in scenarios)
        {
            string name = "commit-" + scenario.Mode + ".bin", path = "/faults/" + name;
            string serverFile = Path.Combine(serverDirectory, name), local = Path.Combine(root, name);
            File.WriteAllBytes(serverFile, previous);
            File.WriteAllBytes(local, replacement);
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, scenario.Mode), false));
            var session = new Session { Protocol = Protocol.SFTP, Host = "127.0.0.1", Username = "easytest", Port = config.RootElement.GetProperty("sftpPort").GetInt32() };
            await using var files = FileTransferFactory.Create(session, store, "easytest-only", _ => true);
            var events = new ConcurrentQueue<ConnectionDiagnostic>();
            files.Diagnostic += events.Enqueue;
            await files.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(10));
            Exception? failure = null;
            var elapsed = Stopwatch.StartNew();
            try { await files.UploadAsync(local, path, CancellationToken.None, TimeSpan.FromSeconds(30)).WaitAsync(TimeSpan.FromSeconds(50)); }
            catch (Exception observed) { failure = observed; }
            await files.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            elapsed.Stop();
            bool success = scenario.Mode == "stall-cleanup";
            Console.WriteLine($"INFO: SFTP {scenario.Mode}: {elapsed.ElapsedMilliseconds} ms, {failure?.GetType().Name ?? "success"}");
            Check(File.Exists(Path.Combine(serverDirectory, name + "." + scenario.Marker)) && elapsed.Elapsed < TimeSpan.FromSeconds(14)
                && (success ? failure == null : failure != null && failure is not TimeoutException && events.Any(e => e.Stage == ConnectionStage.Writing && e.ErrorCode is ConnectionErrorCode.NetworkUnavailable or ConnectionErrorCode.ProtocolError))
                && files.State == ConnectionState.Closed && events.All(e => e.OperationId == files.OperationId),
                "SFTP " + scenario.Mode + " preserves the original outcome and bounds recovery/close time");
            Check(scenario.Target == "missing" ? !File.Exists(serverFile)
                    : File.Exists(serverFile) && File.ReadAllBytes(serverFile).SequenceEqual(scenario.Target == "new" ? replacement : previous),
                "SFTP " + scenario.Mode + " leaves the expected target after the fault");
            string[] backups = Directory.GetFiles(serverDirectory, name + ".portharbor-*.bak"), parts = Directory.GetFiles(serverDirectory, name + ".portharbor-*.part");
            Check(backups.Length == (scenario.Backup ? 1 : 0) && parts.Length == (scenario.Part ? 1 : 0)
                && backups.All(file => File.ReadAllBytes(file).SequenceEqual(previous)) && parts.All(file => File.ReadAllBytes(file).SequenceEqual(replacement)),
                "SFTP " + scenario.Mode + " retains intact recovery files without replay or deleting the old backup");

            await using var fresh = FileTransferFactory.Create(session, store, "easytest-only", _ => true);
            await fresh.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(10));
            if (scenario.Target == "missing")
                await fresh.RenameAsync("/faults/" + Path.GetFileName(backups.Single()), path, CancellationToken.None, TimeSpan.FromSeconds(10));
            foreach (var entry in await fresh.ListAsync("/faults", CancellationToken.None, TimeSpan.FromSeconds(10)))
                if (entry.Name.StartsWith(name + ".portharbor-", StringComparison.Ordinal) && (entry.Name.EndsWith(".part", StringComparison.Ordinal) || entry.Name.EndsWith(".bak", StringComparison.Ordinal)))
                    await fresh.DeleteAsync(entry, CancellationToken.None, TimeSpan.FromSeconds(10));
            string recovered = Path.Combine(root, "recovered-" + name);
            await fresh.DownloadAsync(path, recovered, CancellationToken.None, TimeSpan.FromSeconds(10));
            Check(File.ReadAllBytes(recovered).SequenceEqual(scenario.Target == "new" ? replacement : previous)
                && !Directory.GetFiles(serverDirectory, name + ".portharbor-*.*").Any(),
                "SFTP " + scenario.Mode + " permits explicit recovery and cleanup on a fresh connection");
        }
    }
}
