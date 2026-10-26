namespace PortHarbor;

public sealed class SessionService
{
    readonly ISessionRepository repository;

    public SessionService(ISessionRepository repository)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    // Session is mutable. Never expose repository-owned records to an editor.
    public IReadOnlyList<Session> GetSessions() => repository.GetSessions().Select(s => s with { }).ToArray();

    public void Save(Session session, string password)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(password);
        var snapshot = session with { };
        snapshot.Validate();
        repository.Save(snapshot, snapshot.SavePassword ? password : "");
    }

    public void Delete(Guid id) => repository.Delete(id);
}
