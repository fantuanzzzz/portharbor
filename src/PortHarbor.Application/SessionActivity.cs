namespace PortHarbor;

public enum SessionLibraryView { All, Favorites, Recent }
public sealed record RecentSession(Guid SessionId, DateTimeOffset AttemptedAt);

public sealed record SessionActivityState
{
    public const int RecentLimit = 20;
    public Guid[] Favorites { get; init; } = [];
    public RecentSession[] Recent { get; init; } = [];
    public void Validate()
    {
        if (Favorites == null || Recent == null || Favorites.Length > 10000 || Recent.Length > RecentLimit
            || Favorites.Any(id => id == Guid.Empty) || Favorites.Distinct().Count() != Favorites.Length
            || Recent.Any(r => r == null || r.SessionId == Guid.Empty || r.AttemptedAt == default)
            || Recent.Select(r => r.SessionId).Distinct().Count() != Recent.Length)
            throw new ArgumentException("收藏或最近连接数据无效，原文件已保留。");
    }
    public SessionActivityState Snapshot() => this with { Favorites = Favorites.ToArray(), Recent = Recent.ToArray() };
}

public interface ISessionActivityRepository
{
    SessionActivityState Load();
    void Save(SessionActivityState state);
}

public sealed class SessionActivityService(ISessionActivityRepository repository)
{
    SessionActivityState current = new();
    public SessionActivityState Current => current.Snapshot();
    public bool LoadFailed { get; private set; }
    public void Load()
    {
        try { var loaded = repository.Load(); loaded.Validate(); current = loaded.Snapshot(); LoadFailed = false; }
        catch (Exception error) when (IsStorageFailure(error)) { current = new(); LoadFailed = true; }
    }
    void Save(SessionActivityState value)
    {
        value.Validate(); repository.Save(value.Snapshot()); current = value.Snapshot(); LoadFailed = false;
    }
    SessionActivityState Pruned(IEnumerable<Guid> savedIds)
    {
        var ids = savedIds.ToHashSet();
        return new() { Favorites = current.Favorites.Where(ids.Contains).ToArray(), Recent = current.Recent.Where(r => ids.Contains(r.SessionId)).ToArray() };
    }
    public void Reconcile(IEnumerable<Guid> savedIds)
    {
        if (LoadFailed) return; // Preserve unreadable files; explicit changes still report the save failure.
        var value = Pruned(savedIds);
        if (!value.Favorites.SequenceEqual(current.Favorites) || !value.Recent.SequenceEqual(current.Recent)) Save(value);
    }
    public bool ToggleFavorite(Guid id, IEnumerable<Guid> savedIds)
    {
        var ids = savedIds.ToHashSet(); if (!ids.Contains(id) || id == Guid.Empty) return false;
        var value = Pruned(ids); bool add = !value.Favorites.Contains(id);
        Save(value with { Favorites = add ? value.Favorites.Append(id).ToArray() : value.Favorites.Where(x => x != id).ToArray() });
        return add;
    }
    public bool RecordAttempt(Guid id, IEnumerable<Guid> savedIds, DateTimeOffset now)
    {
        var ids = savedIds.ToHashSet(); if (!ids.Contains(id) || id == Guid.Empty) return false;
        var value = Pruned(ids);
        Save(value with { Recent = new[] { new RecentSession(id, now) }.Concat(value.Recent.Where(r => r.SessionId != id)).Take(SessionActivityState.RecentLimit).ToArray() });
        return true;
    }
    public void ClearFavorites(IEnumerable<Guid> savedIds) => Save(Pruned(savedIds) with { Favorites = [] });
    public void ClearRecent(IEnumerable<Guid> savedIds) => Save(Pruned(savedIds) with { Recent = [] });
    public SessionLibrarySnapshot View(IEnumerable<string> groups, IEnumerable<Session> sessions, string query, SessionLibraryView view)
    {
        if (!Enum.IsDefined(view)) throw new ArgumentException("会话视图无效。");
        var saved = sessions.ToDictionary(s => s.Id, s => s with { });
        IEnumerable<Session> chosen = view switch
        {
            SessionLibraryView.Favorites => current.Favorites.Where(saved.ContainsKey).Select(id => saved[id]),
            SessionLibraryView.Recent => current.Recent.Where(r => saved.ContainsKey(r.SessionId)).Select(r => saved[r.SessionId]),
            _ => saved.Values
        };
        var ordered = chosen.ToArray();
        var result = SessionLibrary.Filter(view == SessionLibraryView.All ? groups : [], ordered, query);
        if (view != SessionLibraryView.All) result = result with { Groups = result.Sessions.Select(SessionLibrary.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
        if (view == SessionLibraryView.Recent)
        {
            var matches = result.Sessions.Select(s => s.Id).ToHashSet();
            result = result with { Sessions = ordered.Where(s => matches.Contains(s.Id)).ToArray() };
        }
        return result;
    }
    public static bool IsStorageFailure(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException
        or ArgumentException or System.Security.SecurityException;
}
