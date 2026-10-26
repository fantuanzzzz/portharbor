using System.Text.Json;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void WindowsPathResolution(string root)
    {
        string app = Path.Combine(root, "path-app"); Directory.CreateDirectory(app);
        string local = Path.Combine(root, "local-app-data"); Directory.CreateDirectory(local);
        var defaults = WindowsAppDataPaths.Resolve([], app, local);
        Check(!defaults.IsPortable && defaults.ConfigurationDirectory == Path.Combine(local, "PortHarbor")
            && defaults.ConfigurationFile == Path.Combine(local, "PortHarbor", "settings.json")
            && defaults.LegacyConfigurationFile == Path.Combine(local, "EasyConnect", "settings.json")
            && defaults.TemporaryDirectory == defaults.ConfigurationDirectory,
            "Windows installed paths resolve configuration, legacy source and temporary files consistently");

        File.WriteAllText(Path.Combine(app, "PortHarbor.portable"), "");
        var marked = WindowsAppDataPaths.Resolve([], app, local);
        Check(marked.IsPortable && marked.ConfigurationDirectory == Path.GetFullPath(app)
            && marked.ConfigurationFile == Path.Combine(Path.GetFullPath(app), "PortHarbor.ini")
            && marked.LegacyConfigurationFile == null,
            "portable marker selects the executable directory and never enables installed migration");
        var forcedInstalled = WindowsAppDataPaths.Resolve(["--installed"], app, local);
        Check(!forcedInstalled.IsPortable && forcedInstalled.LegacyConfigurationFile != null,
            "explicit installed mode overrides portable marker and retains legacy migration");

        File.Delete(Path.Combine(app, "PortHarbor.portable"));
        File.WriteAllText(Path.Combine(app, "PortHarbor.ini"), "[General]\nSchemaVersion=1\n");
        Check(WindowsAppDataPaths.Resolve([], app, local).IsPortable, "existing executable-adjacent INI selects portable mode");
        var forcedPortable = WindowsAppDataPaths.Resolve(["--portable"], app, local);
        Check(forcedPortable.IsPortable && forcedPortable.LegacyConfigurationFile == null,
            "explicit portable mode disables installed legacy migration");

        var explicitDir = WindowsAppDataPaths.Resolve(["--data-dir", Path.Combine("relative", "chosen")], app, local);
        Check(explicitDir.IsPortable && explicitDir.ConfigurationDirectory == Path.GetFullPath(Path.Combine("relative", "chosen"))
            && explicitDir.ConfigurationFile == Path.Combine(Path.GetFullPath(Path.Combine("relative", "chosen")), "PortHarbor.ini")
            && explicitDir.LegacyConfigurationFile == null,
            "explicit data directory is normalized and does not opt into legacy migration");
        var explicitInstalled = WindowsAppDataPaths.Resolve(["--installed", "--data-dir", Path.Combine(root, "explicit-installed")], app, local);
        Check(!explicitInstalled.IsPortable && explicitInstalled.LegacyConfigurationFile == null,
            "explicit installed data directory does not copy configuration from the default legacy profile");
        Check(!WindowsAppDataPaths.Resolve(["--portable", "--installed"], app, local).IsPortable,
            "repeated storage mode switches retain last-option precedence");
        Check(Throws<ArgumentException>(() => WindowsAppDataPaths.Resolve(["--unknown"], app, local))
            && Throws<ArgumentException>(() => WindowsAppDataPaths.Resolve(["--data-dir"], app, local)),
            "unknown or incomplete storage arguments are rejected before paths are created");

        string legacyDirectory = Path.Combine(local, "EasyConnect"); Directory.CreateDirectory(legacyDirectory);
        var legacyData = new AppData { SchemaVersion = 1, Groups = ["Legacy/Group"], Sessions = [new Session { Name = "Legacy", Host = "legacy.invalid" }] };
        string legacyFile = Path.Combine(legacyDirectory, "settings.json"); File.WriteAllText(legacyFile, JsonSerializer.Serialize(legacyData, Store.Json));
        using var migrated = new Store(new WindowsSecretProtector(), defaults);
        Check(File.Exists(migrated.FilePath) && migrated.GetSessions().Single().Name == "Legacy"
            && migrated.GetGroups().SequenceEqual(["Legacy/Group"]) && File.Exists(legacyFile),
            "installed-path migration copies the legacy JSON and preserves its original file and user data");
        string explicitPath = Path.Combine(root, "explicit-data");
        var explicitPaths = new AppDataPaths(false, explicitPath, Path.Combine(explicitPath, "settings.json"), null, explicitPath);
        using var explicitStore = new Store(new WindowsSecretProtector(), explicitPaths);
        Check(explicitStore.GetSessions().Count == 0 && !File.Exists(explicitStore.FilePath),
            "explicit data paths ignore a supplied legacy source and do not silently migrate user configuration");

        string portableDir = Path.Combine(root, "portable-paths"); Directory.CreateDirectory(portableDir);
        string portableFile = Path.Combine(portableDir, "PortHarbor.ini"); File.WriteAllText(portableFile, "[General]\nSchemaVersion=1\n");
        using var portable = new Store(new WindowsSecretProtector(), WindowsAppDataPaths.Resolve(["--portable"], portableDir, local));
        Check(portable.FilePath == portableFile && portable.IsPortable && portable.Paths.TemporaryDirectory == portableDir,
            "portable storage retains INI filename and the existing RDP temporary directory");
        string boundedDirectory = Path.Combine(root, "bounded");
        string outsideConfiguration = Path.Combine(root, "outside", "settings.json");
        Check(Path.GetDirectoryName(outsideConfiguration) != boundedDirectory, "path fixture places configuration outside its declared directory");
        Check(Throws<ArgumentException>(() => new Store(new WindowsSecretProtector(), new AppDataPaths(false, boundedDirectory,
            outsideConfiguration, null, boundedDirectory))), "store rejects a configuration path that escapes its configured directory");
    }
}
