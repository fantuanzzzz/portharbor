namespace PortHarbor;

public enum PlatformCapability { SecretProtection, LocalShell, SerialPort, RemoteDesktop }
public enum PlatformUnavailableReason { None, UnsupportedOperatingSystem, MissingSystemComponent, InspectionFailed }

/// <summary>Reports runtime prerequisites, not device access, authentication or connection success.</summary>
public sealed record PlatformCapabilityStatus(bool IsAvailable, PlatformUnavailableReason Reason)
{
    public static PlatformCapabilityStatus Available { get; } = new(true, PlatformUnavailableReason.None);
}

public interface IPlatformCapabilities
{
    PlatformCapabilityStatus Get(PlatformCapability capability);
}

/// <summary>Network protocols do not depend on local terminal or desktop capabilities.</summary>
public sealed class PlatformConnectionPolicy(IPlatformCapabilities capabilities)
{
    public PlatformCapabilityStatus GetAvailability(Protocol protocol) => protocol switch
    {
        Protocol.Shell => capabilities.Get(PlatformCapability.LocalShell),
        Protocol.Serial => capabilities.Get(PlatformCapability.SerialPort),
        Protocol.RDP => capabilities.Get(PlatformCapability.RemoteDesktop),
        _ => PlatformCapabilityStatus.Available
    };
}
