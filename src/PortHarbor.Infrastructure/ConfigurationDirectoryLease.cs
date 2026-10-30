namespace PortHarbor;

/// <summary>One application writer per configuration directory, released by the OS on exit.</summary>
public sealed class ConfigurationDirectoryLease : IDisposable
{
    readonly FileStream handle;

    ConfigurationDirectoryLease(FileStream handle) => this.handle = handle;

    public static ConfigurationDirectoryLease Acquire(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        try
        {
            return new(new FileStream(Path.Combine(directory, ".portharbor.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        {
            throw new IOException("此配置目录已由另一个 PortHarbor 窗口使用。请切换到已有窗口，或使用 --data-dir 指定其他目录。", error);
        }
    }

    // Keep the empty file: deleting it after releasing the handle would race the next owner.
    public void Dispose() => handle.Dispose();
}
