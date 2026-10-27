namespace PortHarbor;

/// <summary>Transfer data has no vault, credential or host-trust fields. Sessions are copied at port boundaries.</summary>
public sealed record SessionConfiguration(IReadOnlyList<string> Groups, IReadOnlyList<Session> Sessions);

public interface IConfigurationTransfer
{
    SessionConfiguration ReadSessionFile(string path);
    void WriteSessionFile(string path, SessionConfiguration configuration);
    SessionConfiguration GetSessionConfiguration();
    /// <summary>Runs a pure change against the latest snapshot under the storage lock, saves once,
    /// and leaves both stored and in-memory configuration unchanged on failure.</summary>
    void UpdateSessionConfiguration(Func<SessionConfiguration, SessionConfiguration> change);
}

/// <summary>Owns merge, identity and password-saving policy; adapters own formats and atomic persistence.</summary>
public sealed class ConfigurationTransferService(IConfigurationTransfer storage)
{
    readonly IConfigurationTransfer storage = storage ?? throw new ArgumentNullException(nameof(storage));

    public void Export(string path)
    {
        var configuration = Copy(storage.GetSessionConfiguration(), clearPasswordSaving: true);
        storage.WriteSessionFile(path, configuration);
    }

    public int Import(string path)
    {
        var incoming = Copy(storage.ReadSessionFile(path), clearPasswordSaving: true);
        if (incoming.Sessions.Select(s => s.Id).Distinct().Count() != incoming.Sessions.Count)
            throw new InvalidDataException("会话 ID 重复。");
        storage.UpdateSessionConfiguration(current =>
        {
            var existing = Copy(current, clearPasswordSaving: false);
            var usedIds = existing.Sessions.Concat(incoming.Sessions).Select(s => s.Id).ToHashSet();
            var imported = incoming.Sessions.Select(session =>
            {
                Guid id;
                do { id = Guid.NewGuid(); } while (!usedIds.Add(id));
                return session with { Id = id };
            }).ToArray();
            return new SessionConfiguration(
                existing.Groups.Concat(incoming.Groups).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                existing.Sessions.Concat(imported).ToArray());
        });
        return incoming.Sessions.Count;
    }

    static SessionConfiguration Copy(SessionConfiguration configuration, bool clearPasswordSaving)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Groups == null || configuration.Sessions == null
            || configuration.Groups.Any(g => string.IsNullOrWhiteSpace(g) || g.Any(char.IsControl))
            || configuration.Sessions.Any(s => s == null))
            throw new InvalidDataException("配置结构损坏。");
        var sessions = configuration.Sessions.Select(s => s with { SavePassword = !clearPasswordSaving && s.SavePassword }).ToArray();
        foreach (var session in sessions) session.Validate();
        return new SessionConfiguration(configuration.Groups.ToArray(), sessions);
    }
}
