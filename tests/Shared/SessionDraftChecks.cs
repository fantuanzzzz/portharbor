using PortHarbor;

namespace PortHarbor.Checks;

internal static class SessionDraftChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var protocol in new[] { Protocol.SSH, Protocol.Telnet, Protocol.RDP, Protocol.FTP, Protocol.SFTP })
        {
            var source = new Session { Name = "  ", Protocol = protocol, Host = "  2001:db8::42  " };
            var draft = SessionDraftDefaults.ApplyName(source);
            check(draft.Name == "2001:db8::42" && source.Name == "  " && draft.Id == source.Id,
                protocol + " unnamed drafts use the host without changing the source or identity");
        }
        var named = new Session { Name = "Production", Host = "old.invalid" };
        check(SessionDraftDefaults.ApplyName(named with { Host = "new.invalid" }).Name == "Production",
            "an explicit session name remains independent of host edits");
        check(SessionDraftDefaults.ApplyName(new Session { Name = "", Protocol = Protocol.Serial, SerialPort = "COM42", Host = "stale.invalid" }).Name == "COM42"
            && SessionDraftDefaults.ApplyName(new Session { Name = "", Protocol = Protocol.Shell, Executable = @"C:\Program Files\PowerShell\pwsh.exe", Host = "stale.invalid" }).Name == "pwsh.exe",
            "serial and shell defaults use their active target rather than a hidden host draft");
        foreach (var protocol in new[] { Protocol.SSH, Protocol.SFTP })
        {
            var legacy = new Session { Host = "fixture.invalid", Protocol = protocol, Username = " " };
            check(legacy.GetValidationErrors().Count == 0 && legacy.GetValidationErrors(requireAuthentication: true).Single().Field == "Username",
                protocol + " legacy configuration remains readable while interactive use requires a username");
            check((legacy with { Username = "operator" }).GetValidationErrors(requireAuthentication: true).Count == 0,
                protocol + " explicit username clears its authentication validation error");
        }
        check(new[] { Protocol.Telnet, Protocol.RDP, Protocol.FTP, Protocol.Serial, Protocol.Shell }.All(protocol =>
            new Session { Protocol = protocol, Host = "fixture.invalid", Username = "" }.GetValidationErrors(requireAuthentication: true).Count == 0),
            "SSH username requirements do not spill into other protocols");
    }
}
