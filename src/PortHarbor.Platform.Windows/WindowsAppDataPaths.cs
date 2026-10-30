namespace PortHarbor;

public sealed class WindowsAppDataPaths : IAppDataPaths
{
    public bool IsPortable { get; }
    public string ConfigurationDirectory { get; }
    public string ConfigurationFile { get; }
    public string? LegacyConfigurationFile { get; }
    // Keep the existing RDP location for both modes; do not silently fall back to the system temp folder.
    public string TemporaryDirectory => ConfigurationDirectory;

    public WindowsAppDataPaths(string? directory = null, bool portable = false)
        : this(directory, portable, AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), directory == null) { }

    internal WindowsAppDataPaths(string? directory, bool portable, string baseDirectory, string localAppData, bool allowLegacyMigration = true)
    {
        IsPortable = portable;
        if (directory != null) ConfigurationDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        else if (portable) ConfigurationDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        else
        {
            if (string.IsNullOrWhiteSpace(localAppData)) throw new IOException("无法确定 Windows 用户数据目录。");
            ConfigurationDirectory = Path.Combine(Path.GetFullPath(localAppData), "PortHarbor");
            if (allowLegacyMigration) LegacyConfigurationFile = Path.Combine(Path.GetFullPath(localAppData), "EasyConnect", "settings.json");
        }
        ConfigurationFile = Path.Combine(ConfigurationDirectory, portable ? "PortHarbor.ini" : "settings.json");
    }

    public static WindowsAppDataPaths Resolve(string[] args, string? baseDirectory = null, bool defaultPortable = false)
        => Resolve(args, baseDirectory ?? AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), defaultPortable);

    internal static WindowsAppDataPaths Resolve(string[] args, string baseDirectory, string localAppData, bool defaultPortable = false)
    {
        ArgumentNullException.ThrowIfNull(args);
        baseDirectory = Path.GetFullPath(baseDirectory);
        bool portable = !File.Exists(Path.Combine(baseDirectory, "PortHarbor.installed"))
            && (defaultPortable || File.Exists(Path.Combine(baseDirectory, "PortHarbor.portable")) || File.Exists(Path.Combine(baseDirectory, "PortHarbor.ini")));
        string? directory = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--portable") portable = true;
            else if (args[i] == "--installed") portable = false;
            else if (args[i] == "--data-dir" && i + 1 < args.Length) directory = Path.GetFullPath(args[++i]);
            else throw new ArgumentException("Invalid startup argument: " + args[i]);
        }
        return new WindowsAppDataPaths(directory, portable, baseDirectory, localAppData, directory == null);
    }
}
