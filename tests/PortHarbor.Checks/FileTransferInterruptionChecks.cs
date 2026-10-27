using System.Collections.Concurrent;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static async Task FileTransferInterruptions(string root, string configPath)
    {
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        string fixtureRoot = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        byte[] content = Enumerable.Range(0, 2 * 1024 * 1024).Select(i => (byte)(i % 251)).ToArray();
        byte[] previous = "previous target contents"u8.ToArray();
        foreach (var protocol in new[] { Protocol.FTP, Protocol.SFTP })
        foreach (string mode in protocol == Protocol.FTP ? new[] { "download", "upload", "confirmation" } : new[] { "download", "upload" })
        {
            bool upload = mode == "upload";
            string name = mode == "confirmation" ? "reject-download.bin" : upload ? "drop-upload.bin" : "drop-download.bin";
            string serverDirectory = Path.Combine(fixtureRoot, protocol.ToString().ToLowerInvariant(), "faults");
            Directory.CreateDirectory(serverDirectory);
            string serverFile = Path.Combine(serverDirectory, name);
            File.WriteAllBytes(serverFile, upload ? previous : content);
            string local = Path.Combine(root, protocol + "-" + name);
            File.WriteAllBytes(local, upload ? content : previous);
            string marker = Path.Combine(serverDirectory, mode == "confirmation" ? "confirmation.rejected" : mode + ".dropped");
            if (File.Exists(marker)) File.Delete(marker);
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "transfer-drop-" + protocol + mode), false));
            var session = new Session
            {
                Protocol = protocol, Host = "127.0.0.1", Username = "easytest",
                Port = config.RootElement.GetProperty(protocol.ToString().ToLowerInvariant() + "Port").GetInt32()
            };
            await using var files = FileTransferFactory.Create(session, store, "easytest-only", _ => true);
            var events = new ConcurrentQueue<ConnectionDiagnostic>();
            files.Diagnostic += events.Enqueue;
            await files.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(10));
            Exception? failure = null;
            try
            {
                Task operation = upload
                    ? files.UploadAsync(local, "/faults/" + name, CancellationToken.None, TimeSpan.FromSeconds(10))
                    : files.DownloadAsync("/faults/" + name, local, CancellationToken.None, TimeSpan.FromSeconds(10));
                await operation.WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException) { throw; }
            catch (Exception observed) { failure = observed; }
            await files.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            string scenario = protocol + (mode == "confirmation" ? " rejected download confirmation" : upload ? " interrupted upload" : " interrupted download");
            Check(File.Exists(marker) && failure != null && events.Any(e => e.Stage == (upload ? ConnectionStage.Writing : ConnectionStage.Reading)
                    && e.ErrorCode is ConnectionErrorCode.NetworkUnavailable or ConnectionErrorCode.ProtocolError)
                && events.All(e => e.OperationId == files.OperationId) && files.State == ConnectionState.Closed,
                scenario + " reports its original transfer stage and closes within the deadline");
            Check(File.ReadAllBytes(upload ? serverFile : local).SequenceEqual(previous)
                && (upload || !Directory.EnumerateFiles(root, Path.GetFileName(local) + ".portharbor-*.part").Any()),
                scenario + " preserves the previous target and removes local download staging");

            // Inspect the private fixture directory directly: a disconnected control
            // session cannot promise to delete a partial remote upload.
            var leftovers = Directory.GetFiles(serverDirectory, name + ".portharbor-*.part");
            Check(upload ? leftovers.All(file => new FileInfo(file).Length > 0 && new FileInfo(file).Length < content.Length)
                    : File.ReadAllBytes(serverFile).SequenceEqual(content),
                scenario + (upload ? " leaves only identifiable partial uploads when remote cleanup is unavailable" : " leaves the remote source unchanged"));
            await using var fresh = FileTransferFactory.Create(session, store, "easytest-only", _ => true);
            await fresh.ConnectAsync(CancellationToken.None, TimeSpan.FromSeconds(10));
            foreach (var entry in await fresh.ListAsync("/faults", CancellationToken.None, TimeSpan.FromSeconds(10)))
                if (entry.Name.StartsWith(name + ".portharbor-", StringComparison.Ordinal) && entry.Name.EndsWith(".part", StringComparison.Ordinal))
                    await fresh.DeleteAsync(entry, CancellationToken.None, TimeSpan.FromSeconds(10));
            Check(!Directory.GetFiles(serverDirectory, name + ".portharbor-*.part").Any(),
                scenario + " allows explicit cleanup through a new connection without replaying the transfer");
        }
    }
}
