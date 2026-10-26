using Renci.SshNet;

namespace PortHarbor;

internal sealed class SshTerminalTransport(Session session, IHostKeyTrust trust, string password, Func<string, bool> confirm) : StreamTerminalTransport
{
    ShellStream? shell;
    protected override async Task OpenAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token)
    {
        reportStage(ConnectionStage.Authenticating);
        var info = SshClientSupport.SshInfo(session, password, out var key);
        if (key != null) Own(key);
        var client = Own(new SshClient(info));
        SshClientSupport.HostKey(client, session, trust, confirm, reportStage);
        reportStage(ConnectionStage.Connecting);
        await client.ConnectAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        reportStage(ConnectionStage.OpeningChannel);
        // SSH.NET exposes synchronous shell-channel creation; closing the owned client interrupts it.
        shell = Own(await Task.Run(() => client.CreateShellStream("vt100", (uint)columns, (uint)rows, 0, 0, 32768), token).ConfigureAwait(false));
        Input = Output = shell;
    }
    public override async ValueTask ResizeAsync(int columns, int rows, CancellationToken token)
        => await Task.Run(() => shell?.ChangeWindowSize((uint)columns, (uint)rows, 0, 0), token).ConfigureAwait(false);
}
