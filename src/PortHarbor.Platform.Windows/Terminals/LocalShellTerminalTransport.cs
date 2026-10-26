namespace PortHarbor;

internal sealed class LocalShellTerminalTransport(Session session) : StreamTerminalTransport
{
    ConPty? pty;
    protected override Task OpenAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        reportStage(ConnectionStage.OpeningChannel);
        pty = Own(new ConPty(session, columns, rows));
        Input = pty.Input;
        Output = pty.Output;
        return Task.CompletedTask;
    }

    public override ValueTask ResizeAsync(int columns, int rows, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        pty?.Resize(columns, rows);
        return ValueTask.CompletedTask;
    }
}
