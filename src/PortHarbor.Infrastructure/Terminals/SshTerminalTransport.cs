using Renci.SshNet;
using System.Runtime.ExceptionServices;

namespace PortHarbor;

internal sealed class SshTerminalTransport(SshProtocolConfiguration session, IHostKeyTrust trust, string password, Func<string, bool> confirm) : StreamTerminalTransport
{
    ShellStream? shell;
    ExceptionDispatchInfo? connectionFailure;
    protected override async Task OpenAsync(int columns, int rows, Action<ConnectionStage> reportStage, CancellationToken token)
    {
        reportStage(ConnectionStage.Authenticating);
        var info = SshClientSupport.SshInfo(session.Host, session.Port, session.Username, session.PrivateKey, password, out var key);
        if (key != null) Own(key);
        var client = Own(new SshClient(info));
        SshClientSupport.HostKey(client, session.Host, session.Port, trust, confirm, reportStage);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
        reportStage(ConnectionStage.Connecting);
        await client.ConnectAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        client.ErrorOccurred += (_, args) =>
        {
            Interlocked.CompareExchange(ref connectionFailure, ExceptionDispatchInfo.Capture(args.Exception), null);
            // ShellStream's blocking read can outlive a lost SSH session. Closing its
            // owned resources wakes it; the read then reports the original failure.
            Abort();
        };
        reportStage(ConnectionStage.OpeningChannel);
        // SSH.NET exposes synchronous shell-channel creation; closing the owned client interrupts it.
        try
        {
            shell = Own(await Task.Run(() => client.CreateShellStream("vt100", (uint)columns, (uint)rows, 0, 0, 32768), token).ConfigureAwait(false));
        }
        catch
        {
            Volatile.Read(ref connectionFailure)?.Throw();
            throw;
        }
        Input = Output = shell;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token)
    {
        try
        {
            int count = await base.ReadAsync(buffer, token).ConfigureAwait(false);
            Volatile.Read(ref connectionFailure)?.Throw();
            return count;
        }
        catch
        {
            Volatile.Read(ref connectionFailure)?.Throw();
            throw;
        }
    }
    public override async ValueTask ResizeAsync(int columns, int rows, CancellationToken token)
        => await Task.Run(() => shell?.ChangeWindowSize((uint)columns, (uint)rows, 0, 0), token).ConfigureAwait(false);
}
