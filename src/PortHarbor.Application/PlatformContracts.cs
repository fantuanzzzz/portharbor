namespace PortHarbor;

/// <summary>Protects secrets for the current platform/user; protected values are not portable.</summary>
public interface ISecretProtector
{
    string Protect(string value);
    string Unprotect(string value);
}

/// <summary>Starts an external desktop client, reporting Writing and LaunchingClient stages.
/// Success means client startup only. The adapter owns temporary configuration cleanup.</summary>
public interface IRemoteDesktopLauncher
{
    void Launch(string configuration, string directory, Action<ConnectionStage> reportStage);
}
