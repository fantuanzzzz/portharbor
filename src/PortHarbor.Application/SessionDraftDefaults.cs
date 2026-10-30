namespace PortHarbor;

public static class SessionDraftDefaults
{
    public static Session ApplyName(Session draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!string.IsNullOrWhiteSpace(draft.Name)) return draft with { };
        string name = draft.Protocol switch
        {
            Protocol.Serial => draft.SerialPort.Trim(),
            Protocol.Shell => draft.Executable.Trim().Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "",
            _ => draft.Host.Trim()
        };
        return draft with { Name = name };
    }
}
