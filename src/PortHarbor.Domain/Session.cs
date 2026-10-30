using System.Text.Json.Serialization;

namespace PortHarbor;

public enum Protocol { SSH, Telnet, RDP, FTP, SFTP, Serial, Shell }

public sealed record SessionValidationError(string Field, string Message);

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
        var errors = GetValidationErrors();
        if (errors.Count > 0) throw new ArgumentException(errors[0].Message);
    }

    public IReadOnlyList<SessionValidationError> GetValidationErrors()
    {
        var errors = new List<SessionValidationError>();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add(new(nameof(Name), "请输入会话名称。"));
        if (!Enum.IsDefined(Protocol)) { errors.Add(new(nameof(Protocol), "未知协议。")); return errors; }
        if (Protocol is not (Protocol.Serial or Protocol.Shell))
        {
            if (string.IsNullOrWhiteSpace(Host) || Host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) errors.Add(new(nameof(Host), "请输入有效主机名或 IP（不包含协议前缀和空格）。"));
            if (Port is < 1 or > 65535) errors.Add(new(nameof(Port), "端口必须在 1–65535 之间。"));
        }
        if (Protocol == Protocol.Serial)
        {
            if (string.IsNullOrWhiteSpace(SerialPort)) errors.Add(new(nameof(SerialPort), "请输入串口名称，可手动输入 COM 端口。"));
            if (BaudRate <= 0) errors.Add(new(nameof(BaudRate), "波特率必须大于 0。"));
            if (DataBits is < 5 or > 8) errors.Add(new(nameof(DataBits), "数据位必须在 5–8 之间。"));
        }
        if (Protocol == Protocol.Shell && string.IsNullOrWhiteSpace(Executable)) errors.Add(new(nameof(Executable), "请输入 Shell 程序路径。"));
        if (Protocol == Protocol.RDP && Host.Contains(':') && !System.Net.IPAddress.TryParse(Host, out _)) errors.Add(new(nameof(Host), "RDP 主机名中不能包含端口；请使用单独的端口字段。"));
        return errors;
    }

    public static int DefaultPort(Protocol p) => p switch { Protocol.Telnet => 23, Protocol.RDP => 3389, Protocol.FTP => 21, _ => 22 };
}
