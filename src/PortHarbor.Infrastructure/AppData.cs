namespace PortHarbor;

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 2;
    public List<string> Groups { get; set; } = [];
    public VaultSettings? Vault { get; set; }
    public List<Session> Sessions { get; set; } = [];
    public Dictionary<string, string> Credentials { get; set; } = [];
    public Dictionary<string, string> HostKeys { get; set; } = [];
}
