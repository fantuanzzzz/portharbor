using System.IO.Ports;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Renci.SshNet;

namespace PortHarbor;

public sealed class TerminalConnection : IDisposable
{
    readonly Session session;
    readonly Store store;
    readonly string password;
    readonly Func<string, bool> confirm;
    readonly CancellationTokenSource stop = new();
    readonly object writeGate = new();
    readonly Channel<byte[]> writes = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    Stream? input, output;
    SshClient? ssh; ShellStream? shell;
    SerialPort? serial; TcpClient? tcp; ConPty? pty; PrivateKeyFile? key;
    TelnetCodec? telnet;
    int disposed;
    public event Action<string>? Output;
    public TerminalConnection(Session session, Store store, string password, Func<string, bool> confirm) { this.session = session; this.store = store; this.password = password; this.confirm = confirm; }
    public static ConnectionInfo SshInfo(Session s, string password, out PrivateKeyFile? key)
    {
        key = null;
        AuthenticationMethod auth;
        if (!string.IsNullOrWhiteSpace(s.PrivateKey)) { key = password.Length == 0 ? new PrivateKeyFile(s.PrivateKey) : new PrivateKeyFile(s.PrivateKey, password); auth = new PrivateKeyAuthenticationMethod(s.Username, key); }
        else auth = new PasswordAuthenticationMethod(s.Username, password);
        return new ConnectionInfo(s.Host, s.Port, s.Username, auth) { Timeout = TimeSpan.FromSeconds(15) };
    }
    public static void HostKey(BaseClient client, Session session, Store store, Func<string, bool> confirm)
    {
        client.HostKeyReceived += (_, e) => e.CanTrust = store.CheckHostKey(session.Host, session.Port, "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('='), confirm);
        client.KeepAliveInterval = TimeSpan.FromSeconds(30);
    }
    public async Task Run(int columns, int rows)
    {
        try
        {
            await Task.Run(() =>
            {
                stop.Token.ThrowIfCancellationRequested();
                switch (session.Protocol)
                {
                    case Protocol.Shell: pty = new ConPty(session, columns, rows); input = pty.Input; output = pty.Output; break;
                    case Protocol.SSH:
                        ssh = new SshClient(SshInfo(session, password, out key));
                        HostKey(ssh, session, store, confirm); ssh.Connect();
                        stop.Token.ThrowIfCancellationRequested();
                        shell = ssh.CreateShellStream("vt100", (uint)columns, (uint)rows, 0, 0, 32768); input = output = shell; break;
                    case Protocol.Serial:
                        serial = new SerialPort(session.SerialPort, session.BaudRate, Enum.Parse<Parity>(session.Parity), session.DataBits, Enum.Parse<StopBits>(session.StopBits)) { Handshake = Enum.Parse<Handshake>(session.Handshake), WriteTimeout = 5000 };
                        serial.Open(); input = output = serial.BaseStream; break;
                    case Protocol.Telnet:
                        tcp = new TcpClient();
                        tcp.ConnectAsync(session.Host, session.Port, stop.Token).AsTask().GetAwaiter().GetResult();
                        input = output = tcp.GetStream(); telnet = new TelnetCodec(WriteRaw); break;
                    default: throw new NotSupportedException();
                }
            }, stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            _ = Task.Run(async () =>
            {
                try { await foreach (var bytes in writes.Reader.ReadAllAsync(stop.Token)) WriteRaw(bytes); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Output?.Invoke("\r\n[发送失败：" + e.Message + "]\r\n"); Dispose(); }
            });
            Output?.Invoke("\x1b[32m[已连接；Ctrl+Shift+C 复制，Ctrl+Shift+V 粘贴]\x1b[0m\r\n");
            var buffer = new byte[16384]; var decoder = Encoding.UTF8.GetDecoder(); var chars = new char[32768];
            while (true)
            {
                var currentOutput = output!;
                int n = await Task.Run(() => currentOutput.Read(buffer, 0, buffer.Length));
                if (n == 0) break;
                byte[] bytes = telnet?.Decode(buffer.AsSpan(0, n)) ?? buffer[..n];
                int count = decoder.GetChars(bytes, 0, bytes.Length, chars, 0);
                Output?.Invoke(new string(chars, 0, count));
            }
        }
        catch (Exception e) when (stop.IsCancellationRequested) { System.Diagnostics.Debug.WriteLine(e.GetType().Name); }
        finally { writes.Writer.TryComplete(); stop.Cancel(); await Task.Run(CloseResources); Output?.Invoke("\r\n\x1b[90m[连接已关闭]\x1b[0m\r\n"); }
    }
    void WriteRaw(byte[] bytes) { lock (writeGate) { input?.Write(bytes); input?.Flush(); } }
    public void Send(byte[] bytes)
    {
        if (disposed != 0) return;
        var data = session.Protocol == Protocol.Telnet ? TelnetCodec.Encode(bytes) : session.Protocol == Protocol.Serial ? bytes.Select(b => b == 127 ? (byte)8 : b).ToArray() : bytes;
        if (!writes.Writer.TryWrite(data)) Output?.Invoke("\r\n[输入队列已满，请稍后重试]\r\n");
    }
    public void Resize(int columns, int rows) { try { pty?.Resize(columns, rows); shell?.ChangeWindowSize((uint)columns, (uint)rows, 0, 0); } catch { /* resize may race with disconnection */ } }
    readonly object closeGate = new();
    void CloseResources()
    {
        lock (closeGate)
        {
            // Close all handles even when a previous peer has already closed its stream.
            foreach (IDisposable? item in new IDisposable?[] { tcp, serial, shell, ssh, pty, key }) { try { item?.Dispose(); } catch { } }
            tcp = null; serial = null; shell = null; ssh = null; pty = null; key = null; input = output = null;
        }
    }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; stop.Cancel(); _ = Task.Run(CloseResources); }
}
