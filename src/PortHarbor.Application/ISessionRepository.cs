namespace PortHarbor;

/// <summary>Persists sessions and their associated credentials as one operation.</summary>
public interface ISessionRepository
{
    IReadOnlyList<Session> GetSessions();

    /// <summary>
    /// Inserts or replaces a session by ID. An empty password or disabled SavePassword
    /// removes its saved credential. Failure must preserve the previous state.
    /// Password protection belongs to the adapter; plaintext must not be persisted.
    /// </summary>
    void Save(Session session, string password);

    /// <summary>Removes a session and its credential together; missing IDs are allowed.</summary>
    void Delete(Guid id);
}
