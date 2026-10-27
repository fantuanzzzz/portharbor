namespace PortHarbor;

public static class GroupPath
{
    public static string Validate(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Any(char.IsControl) || name.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part != part.Trim() || part is "." or "..") || name.Contains('\\'))
            throw new ArgumentException("请输入有效的分类名称。");
        return name;
    }
}

public sealed record SessionLibrarySnapshot(IReadOnlyList<string> Groups, IReadOnlyList<Session> Sessions);

public static class SessionLibrary
{
    public static SessionLibrarySnapshot Filter(IEnumerable<string> groups, IEnumerable<Session> sessions, string query)
    {
        string filter = query.Trim();
        var all = sessions.Select(s => s with { }).ToArray();
        var visible = all.Where(s => filter.Length == 0 || $"{s.Name} {s.Host} {s.Protocol} {Group(s)}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name).ToArray();
        var paths = groups.Concat(all.Select(Group)).Append("默认").Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(g => filter.Length == 0 || g.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Concat(visible.Select(Group)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).ToArray();
        return new(paths, visible);
    }

    public static string Group(Session session) => string.IsNullOrWhiteSpace(session.Group) ? "默认" : session.Group;
}
