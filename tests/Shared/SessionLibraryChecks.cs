using PortHarbor;

namespace PortHarbor.Checks;

internal static class SessionLibraryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var session = new Session { Name = "Core", Host = "switch.invalid", Group = "Work/Switches/Core" };
        var result = SessionLibrary.Filter(["Empty", "Work"], [session], "switch.invalid");
        check(result.Sessions.Count == 1 && result.Groups.Contains(session.Group) && !result.Groups.Contains("Empty"),
            "session library search preserves the matched nested group independently of UI controls");
        result.Sessions[0].Name = "changed";
        check(session.Name == "Core" && SessionLibrary.Filter(["Empty"], [session], "").Groups.Contains("Empty"),
            "session library snapshots preserve empty groups and do not mutate saved sessions");
        check(GroupPath.Validate(" Work/Core ") == "Work/Core" && new[] { "a//b", "a/../b", "a\\b", "a/ b" }.All(name =>
            { try { GroupPath.Validate(name); return false; } catch (ArgumentException) { return true; } }),
            "group validation rejects invalid path segments outside the storage and UI implementations");
    }
}
