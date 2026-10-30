using System.Diagnostics;
using System.Text.Json;

namespace PortHarbor;

/// <summary>Only managed, unlocked workspaces with no surviving client may be swept.</summary>
internal sealed class RdpTemporaryWorkspace : IDisposable
{
    const string Marker = "PortHarbor.RdpTemporary.v1";
    static readonly string[] OwnedFiles = ["connection.rdp", "owner.json", "owner.json.tmp", ".owner.lock"];
    readonly string directory;
    readonly FileStream lease;
    bool disposed;
    internal string ConfigurationPath => Path.Combine(directory, "connection.rdp");
    internal string DirectoryPath => directory;
    internal sealed record Owner(string Kind, string State, int ClientId = 0, long ClientStartedUtcTicks = 0);

    RdpTemporaryWorkspace(string directory, FileStream lease) { this.directory = directory; this.lease = lease; }

    internal static RdpTemporaryWorkspace Create(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory)) throw new DirectoryNotFoundException("远程桌面临时目录不存在。");
        string root = Path.Combine(Path.GetFullPath(dataDirectory), "rdp-temporary");
        Directory.CreateDirectory(root);
        if (IsLink(root)) throw new IOException("远程桌面临时目录不能是重解析点。");
        Sweep(dataDirectory);
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var workspace = new RdpTemporaryWorkspace(directory,
            new FileStream(Path.Combine(directory, ".owner.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
        try { workspace.WriteOwner(new(Marker, "created")); return workspace; }
        catch { workspace.Dispose(); throw; }
    }

    internal void MarkLaunching() => WriteOwner(new(Marker, "launching"));

    internal void RecordClient(Process process)
    {
        try { WriteOwner(new(Marker, "started", process.Id, process.StartTime.ToUniversalTime().Ticks)); }
        catch (InvalidOperationException) { WriteOwner(new(Marker, "exited")); }
        catch (System.ComponentModel.Win32Exception)
        {
            // Leave "launching" when process identity cannot be established; never guess ownership.
        }
    }

    void WriteOwner(Owner owner)
    {
        string path = Path.Combine(directory, "owner.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(owner));
        File.Move(path + ".tmp", path, true);
    }

    internal static void Sweep(string dataDirectory, Func<int, long, bool?>? isClientRunning = null)
    {
        string root = Path.Combine(Path.GetFullPath(dataDirectory), "rdp-temporary");
        isClientRunning ??= IsClientRunning;
        try
        {
            if (!Directory.Exists(root) || IsLink(root)) return;
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                FileStream? handle = null;
                try
                {
                    if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _) || IsLink(directory)) continue;
                    string lockPath = Path.Combine(directory, ".owner.lock");
                    if (!File.Exists(lockPath) || IsLink(lockPath)) continue;
                    handle = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    if (!OnlyOwnedFiles(directory)) continue;
                    string path = Path.Combine(directory, "owner.json");
                    if (new FileInfo(path).Length > 4096) continue;
                    var owner = JsonSerializer.Deserialize<Owner>(File.ReadAllText(path));
                    if (owner?.Kind != Marker) continue;
                    bool removable = owner.State is "created" or "exited" ||
                        (owner.State == "started" && owner.ClientId > 0 && owner.ClientStartedUtcTicks > 0 &&
                            isClientRunning(owner.ClientId, owner.ClientStartedUtcTicks) == false);
                    if (!removable) continue;
                    using var abandoned = new RdpTemporaryWorkspace(directory, handle);
                    handle = null;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
                finally { handle?.Dispose(); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    static bool? IsClientRunning(int id, long started)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    static bool OnlyOwnedFiles(string directory) => Directory.EnumerateFileSystemEntries(directory).All(path =>
        OwnedFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal) && !Directory.Exists(path) && !IsLink(path));

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (OnlyOwnedFiles(directory))
                foreach (string name in OwnedFiles.Where(name => name != ".owner.lock")) File.Delete(Path.Combine(directory, name));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { lease.Dispose(); }
        try
        {
            if (OnlyOwnedFiles(directory))
            {
                File.Delete(Path.Combine(directory, ".owner.lock"));
                Directory.Delete(directory); // Never recurse through user files or directory links.
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
