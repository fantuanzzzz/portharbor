using PortHarbor;

namespace PortHarbor.Checks;

internal static class PlatformCapabilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var capabilities = new FixturePlatformCapabilities();
        var policy = new PlatformConnectionPolicy(capabilities);
        var missing = new PlatformCapabilityStatus(false, PlatformUnavailableReason.MissingSystemComponent);
        foreach (var (protocol, capability) in new[] { (Protocol.Shell, PlatformCapability.LocalShell),
            (Protocol.Serial, PlatformCapability.SerialPort), (Protocol.RDP, PlatformCapability.RemoteDesktop) })
        {
            capabilities.States[capability] = missing; capabilities.Requests.Clear();
            check(policy.GetAvailability(protocol) == missing && capabilities.Requests.SequenceEqual([capability]),
                protocol + " availability queries only its required platform capability");
        }
        capabilities.Requests.Clear();
        check(new[] { Protocol.SSH, Protocol.SFTP, Protocol.FTP, Protocol.Telnet }.All(p => policy.GetAvailability(p).IsAvailable)
            && capabilities.Requests.Count == 0, "network protocols remain available without querying local platform capabilities");
        capabilities.States[PlatformCapability.LocalShell] = PlatformCapabilityStatus.Available;
        check(policy.GetAvailability(Protocol.Shell).IsAvailable, "platform policy rechecks current capability after runtime support recovers");
        capabilities.States[PlatformCapability.SecretProtection] = missing;
        capabilities.States[PlatformCapability.RemoteDesktop] = PlatformCapabilityStatus.Available;
        check(policy.GetAvailability(Protocol.RDP).IsAvailable, "unavailable Windows credential protection does not block starting an external desktop client");
    }
}

internal sealed class FixturePlatformCapabilities : IPlatformCapabilities
{
    internal readonly Dictionary<PlatformCapability, PlatformCapabilityStatus> States = [];
    internal readonly List<PlatformCapability> Requests = [];
    public PlatformCapabilityStatus Get(PlatformCapability capability)
    {
        Requests.Add(capability);
        return States.GetValueOrDefault(capability, PlatformCapabilityStatus.Available);
    }
}
