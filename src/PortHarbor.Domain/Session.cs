using System.Text.Json.Serialization;

namespace PortHarbor;

public enum Protocol { SSH, Telnet, RDP, FTP, SFTP, Serial, Shell }

public sealed record Session
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "新会话";
    public string Group { get; set; } = "默认";
    public Protocol Protocol { get; set; } = Protocol.SSH;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string RemotePath { get; set; } = "/";
    public bool UseTls { get; set; }
    public string SerialPort { get; set; } = "COM1";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "None";
    public string StopBits { get; set; } = "One";
    public string Handshake { get; set; } = "None";
    public string Executable { get; set; } = "powershell.exe";
    public string Arguments { get; set; } = "-NoLogo";
    public string WorkingDirectory { get; set; } = "";
    public bool RdpFullScreen { get; set; }
    public bool Clipboard { get; set; } = true;
    public bool SavePassword { get; set; }
    [JsonIgnore] public ProtocolConfiguration ProtocolConfiguration => SessionProtocolConfiguration.Get(this);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("请输入会话名称。");
        if (!Enum.IsDefined(Protocol)) throw new ArgumentException("未知协议。");
        if (Protocol is not (Protocol.Serial or Protocol.Shell))
        {
            if (string.IsNullOrWhiteSpace(Host) || Host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) throw new ArgumentException("请输入有效主机名或 IP（不包含协议前缀和空格）。");
            if (Port is < 1 or > 65535) throw new ArgumentException("端口必须在 1–65535 之间。");
        }
        if (Protocol == Protocol.Serial && (string.IsNullOrWhiteSpace(SerialPort) || BaudRate <= 0 || DataBits is < 5 or > 8)) throw new ArgumentException("串口参数无效。");
        if (Protocol == Protocol.Shell && string.IsNullOrWhiteSpace(Executable)) throw new ArgumentException("请输入 Shell 程序路径。");
        if (Protocol == Protocol.RDP && (Host.Contains(':') && !System.Net.IPAddress.TryParse(Host, out _))) throw new ArgumentException("RDP 主机名中不能包含端口；请使用单独的端口字段。");
    }

    public static int DefaultPort(Protocol p) => p switch { Protocol.Telnet => 23, Protocol.RDP => 3389, Protocol.FTP => 21, _ => 22 };
}
