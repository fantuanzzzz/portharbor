using PortHarbor;

namespace PortHarbor.Checks;

internal static class SessionActivityChecks
{
    internal sealed class Repository : ISessionActivityRepository
    {
        internal SessionActivityState Value = new();
        internal bool FailLoad, FailSave;
        internal int Writes;
        public SessionActivityState Load() => FailLoad ? throw new InvalidDataException("fixture") : Value.Snapshot();
        public void Save(SessionActivityState value)
        {
            if (FailSave) throw new IOException("fixture");
            Value = value.Snapshot(); Writes++;
        }
    }
    internal static void Run(Action<bool, string> check)
    {
        var a = new Session { Name = "Alpha", Host = "alpha.invalid", Group = "工作/核心" };
        var b = new Session { Name = "Beta", Host = "beta.invalid", Group = "其他" };
        var repo = new Repository(); var service = new SessionActivityService(repo); service.Load();
        check(!service.LoadFailed && service.Current.Favorites.Length == 0 && repo.Writes == 0,
            "session activity defaults do not write storage");
        check(service.ToggleFavorite(a.Id, [a.Id, b.Id]) && service.Current.Favorites.SequenceEqual([a.Id]),
            "saved sessions can be favorited by identity without copying session data");
        var snapshot = service.Current; snapshot.Favorites[0] = b.Id;
        check(service.Current.Favorites.SequenceEqual([a.Id]), "session activity snapshots cannot mutate service-owned favorites");
        check(!service.ToggleFavorite(a.Id, [a.Id, b.Id]) && service.Current.Favorites.Length == 0,
            "toggling a favorite removes only that identity");
        int writes = repo.Writes;
        check(!service.ToggleFavorite(Guid.NewGuid(), [a.Id]) && !service.RecordAttempt(Guid.NewGuid(), [a.Id], DateTimeOffset.UtcNow)
            && repo.Writes == writes, "temporary or missing sessions never create favorite or recent records");
        var now = DateTimeOffset.UtcNow;
        service.ToggleFavorite(a.Id, [a.Id, b.Id]); service.RecordAttempt(a.Id, [a.Id, b.Id], now); service.RecordAttempt(b.Id, [a.Id, b.Id], now.AddSeconds(1));
        service.RecordAttempt(a.Id, [a.Id, b.Id], now.AddSeconds(2));
        check(service.Current.Recent.Select(r => r.SessionId).SequenceEqual([a.Id, b.Id]) && service.Current.Recent[0].AttemptedAt == now.AddSeconds(2),
            "recent attempts deduplicate and move the explicitly retried identity to the front");
        var recent = service.View([], [a, b], "", SessionLibraryView.Recent);
        recent.Sessions[0].Name = "draft";
        check(a.Name == "Alpha" && service.View([], [a with { Name = "Renamed" }, b], "Renamed", SessionLibraryView.Favorites).Sessions.Single().Name == "Renamed",
            "favorite and recent views resolve current session metadata without retaining edited snapshots");
        var filtered = service.View(["Empty"], [a, b], "alpha.invalid", SessionLibraryView.Favorites);
        check(filtered.Sessions.Single().Id == a.Id && filtered.Groups.SequenceEqual([a.Group]),
            "favorite searches retain the matched full category path without unrelated empty groups");
        check(service.View([], [a, b], "工作/核心", SessionLibraryView.Recent).Sessions.Single().Id == a.Id,
            "recent search matches the saved category path and retains attempt order");
        check(service.View([], [b], "", SessionLibraryView.Favorites).Sessions.Count == 0
            && service.View([], [b], "", SessionLibraryView.Recent).Sessions.Single().Id == b.Id,
            "deleted references are invisible even before a cleanup write succeeds");
        repo.FailSave = true; bool failed = false;
        try { service.Reconcile([b.Id]); } catch (IOException) { failed = true; }
        check(failed && service.Current.Favorites.Contains(a.Id) && repo.Value.Favorites.Contains(a.Id),
            "failed reconciliation preserves persisted state and allows a later cleanup retry");
        repo.FailSave = false; service.Reconcile([b.Id]);
        check(service.Current.Favorites.Length == 0 && service.Current.Recent.Select(r => r.SessionId).SequenceEqual([b.Id]),
            "successful reconciliation removes deleted favorite and recent identities");
        var ids = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids) service.RecordAttempt(id, ids, now);
        check(service.Current.Recent.Length == 20 && service.Current.Recent.Select(r => r.SessionId).SequenceEqual(ids.Reverse().Take(20)),
            "recent history keeps only the latest twenty distinct saved-session attempts");
        service.ToggleFavorite(ids[0], ids); service.ClearRecent(ids);
        check(service.Current.Recent.Length == 0 && service.Current.Favorites.SequenceEqual([ids[0]]), "clearing recent history retains favorites");
        service.RecordAttempt(ids[0], ids, now); service.ClearFavorites(ids);
        check(service.Current.Favorites.Length == 0 && service.Current.Recent.Length == 1, "clearing favorites retains recent history");
        repo.FailSave = true; failed = false;
        try { service.ClearRecent(ids); } catch (IOException) { failed = true; }
        check(failed && service.Current.Recent.Length == 1, "failed clearing never publishes an unsaved empty history");
        repo.FailLoad = true; service.Load(); writes = repo.Writes; service.Reconcile(ids);
        check(service.LoadFailed && service.Current.Recent.Length == 0 && repo.Writes == writes,
            "unreadable activity falls back safely and automatic reconciliation does not overwrite it");
        bool Invalid(SessionActivityState value)
        { try { value.Validate(); return false; } catch (ArgumentException) { return true; } }
        check(new[] { new SessionActivityState { Favorites = [Guid.Empty] }, new() { Favorites = [a.Id, a.Id] },
            new() { Recent = [new(a.Id, default)] }, new() { Recent = [new(a.Id, now), new(a.Id, now)] }, new() { Favorites = null! }, new() { Recent = [null!] } }.All(Invalid),
            "invalid activity identities duplicates timestamps and null collections are rejected");
    }
}
