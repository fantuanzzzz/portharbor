namespace PortHarbor;

// Keep a failure visible while the connection publishes its subsequent cleanup states.
internal sealed class ConnectionStatusLabel : Label
{
    bool failed;

    public ConnectionStatusLabel()
    {
        Name = "ConnectionStatus";
        AutoSize = false;
        Height = 30;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(8, 0, 0, 0);
        Text = DiagnosticMessageCatalog.ReadyToConnect;
    }

    public void Report(ConnectionDiagnostic diagnostic)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Report(diagnostic)); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
            return;
        }
        if (diagnostic.State == ConnectionState.Connecting && diagnostic.ErrorCode == ConnectionErrorCode.None) failed = false;
        if (failed && diagnostic.ErrorCode == ConnectionErrorCode.Cancelled) return;
        if (diagnostic.ErrorCode != ConnectionErrorCode.None)
        {
            failed = true;
            Text = DiagnosticPresenter.Error(diagnostic);
        }
        else if (!failed) Text = DiagnosticPresenter.Status(diagnostic);
    }
}
