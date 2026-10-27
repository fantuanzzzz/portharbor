namespace PortHarbor;

public static class DiagnosticPresenter
{
    public static string Status(ConnectionDiagnostic diagnostic)
    {
        if (diagnostic.ErrorCode == ConnectionErrorCode.None)
        {
            if (diagnostic.State == ConnectionState.Closed) return DiagnosticMessageCatalog.State(diagnostic.State);
            if (diagnostic.State == ConnectionState.Connected)
                return diagnostic.Protocol == Protocol.RDP ? DiagnosticMessageCatalog.ExternalDesktopStarted : DiagnosticMessageCatalog.State(diagnostic.State);
        }
        string stage = DiagnosticMessageCatalog.Stage(diagnostic.Stage);
        if (stage.Length == 0) stage = DiagnosticMessageCatalog.State(diagnostic.State);
        if (diagnostic.ErrorCode == ConnectionErrorCode.None) return stage;
        return DiagnosticMessageCatalog.WithError(stage, DiagnosticMessageCatalog.Error(diagnostic.ErrorCode));
    }

    public static string Error(ConnectionDiagnostic diagnostic) =>
        DiagnosticMessageCatalog.WithOperation(Status(diagnostic), diagnostic.OperationId.ToString("N")[..8]);
}
