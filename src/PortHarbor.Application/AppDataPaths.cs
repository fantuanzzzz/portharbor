namespace PortHarbor;

/// <summary>Resolved application locations. Implementations must provide stable absolute paths;
/// resolving paths does not create directories or migrate files.</summary>
public interface IAppDataPaths
{
    bool IsPortable { get; }
    string ConfigurationDirectory { get; }
    string ConfigurationFile { get; }
    string? LegacyConfigurationFile { get; }
    string TemporaryDirectory { get; }
}

/// <summary>Immutable location snapshot, also usable by non-Windows hosts and checks.</summary>
public sealed record AppDataPaths(bool IsPortable, string ConfigurationDirectory, string ConfigurationFile,
    string? LegacyConfigurationFile, string TemporaryDirectory) : IAppDataPaths;
