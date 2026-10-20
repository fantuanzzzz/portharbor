using System.Text.Json;
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

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 1;
    public List<Session> Sessions { get; set; } = [];
    public Dictionary<string, string> Credentials { get; set; } = [];
    public Dictionary<string, string> HostKeys { get; set; } = [];
}

public sealed class Store
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public AppData Data { get; private set; }
    private readonly object gate = new();
    public Store(string? directory = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DirectoryPath = directory ?? Path.Combine(localAppData, "PortHarbor");
        Directory.CreateDirectory(DirectoryPath);
        // Keep the old directory as a one-time migration source for existing users.
        var legacySettings = Path.Combine(localAppData, "EasyConnect", "settings.json");
        if (directory == null && !File.Exists(FilePath) && File.Exists(legacySettings)) File.Copy(legacySettings, FilePath);
        Data = File.Exists(FilePath) ? Read(FilePath) : new();
    }
    static AppData Read(string path)
    {
        var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("配置为空。");
        if (data.SchemaVersion != 1) throw new InvalidDataException("不支持此配置版本。原文件已保留。");
        if (data.Sessions == null || data.Credentials == null || data.HostKeys == null) throw new InvalidDataException("配置结构损坏。原文件已保留。");
        foreach (var s in data.Sessions) s.Validate();
        if (data.Sessions.Select(s => s.Id).Distinct().Count() != data.Sessions.Count) throw new InvalidDataException("会话 ID 重复。");
        return data;
    }
    public void Save()
    {
        lock (gate)
        {
            var temp = FilePath + ".tmp";
            using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(f, Data, Json);
                f.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
    }
    public string GetPassword(Guid id)
    {
        lock (gate) return Data.Credentials.TryGetValue(id.ToString(), out var encrypted) ? SecretProtection.Unprotect(encrypted) : "";
    }
    public void PutSession(Session session, string password)
    {
        lock (gate)
        {
            session.Validate();
            var encrypted = session.SavePassword && password.Length > 0 ? SecretProtection.Protect(password) : null;
            Data.Sessions.RemoveAll(s => s.Id == session.Id);
            Data.Sessions.Add(session);
            if (encrypted != null) Data.Credentials[session.Id.ToString()] = encrypted;
            else Data.Credentials.Remove(session.Id.ToString());
            Save();
        }
    }
    public void Delete(Guid id)
    {
        lock (gate) { Data.Sessions.RemoveAll(s => s.Id == id); Data.Credentials.Remove(id.ToString()); Save(); }
    }
    public bool CheckHostKey(string host, int port, string fingerprint, Func<string, bool> confirm)
    {
        var name = $"[{host.ToLowerInvariant()}]:{port}";
        lock (gate)
        {
            if (Data.HostKeys.TryGetValue(name, out var known))
            {
                if (known != fingerprint) throw new InvalidOperationException($"SSH 主机密钥发生变化，已拒绝连接。\n主机：{name}\n已保存：{known}\n本次：{fingerprint}\n请向服务器管理员核实后，在设置文件中移除该主机的旧记录。");
                return true;
            }
        }
        // A modal UI confirmation must never hold the persistence lock.
        if (!confirm($"首次连接 {name}\n主机密钥指纹：\n{fingerprint}\n\n请与服务器管理员核对。信任并保存此密钥？")) return false;
        lock (gate)
        {
            if (Data.HostKeys.TryGetValue(name, out var concurrent))
            {
                if (concurrent != fingerprint) throw new InvalidOperationException("另一连接已保存不同的 SSH 主机密钥，已拒绝连接。");
                return true;
            }
            Data.HostKeys[name] = fingerprint;
            Save();
            return true;
        }
    }
    public void Export(string path) => File.WriteAllText(path, JsonSerializer.Serialize(new AppData { Sessions = Data.Sessions.Select(s => s with { SavePassword = false }).ToList() }, Json));
    public int Import(string path)
    {
        var imported = Read(path);
        foreach (var s in imported.Sessions) Data.Sessions.Add(s with { Id = Guid.NewGuid(), SavePassword = false });
        Save();
        return imported.Sessions.Count;
    }
}
