using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

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
    public int SchemaVersion { get; set; } = 2;
    public List<string> Groups { get; set; } = [];
    public VaultSettings? Vault { get; set; }
    public List<Session> Sessions { get; set; } = [];
    public Dictionary<string, string> Credentials { get; set; } = [];
    public Dictionary<string, string> HostKeys { get; set; } = [];
}

public sealed class Store : IDisposable
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string DirectoryPath { get; }
    public bool IsPortable { get; }
    public string FilePath => Path.Combine(DirectoryPath, IsPortable ? "PortHarbor.ini" : "settings.json");
    public AppData Data { get; private set; }
    private readonly object gate = new();
    byte[]? vaultKey;
    public bool HasMasterPassword => Data.Vault != null;
    public bool IsVaultUnlocked => vaultKey != null;
    public Store(string? directory = null, bool portable = false)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        IsPortable = portable;
        DirectoryPath = directory ?? (portable ? AppContext.BaseDirectory : Path.Combine(localAppData, "PortHarbor"));
        Directory.CreateDirectory(DirectoryPath);
        // Keep the old directory as a one-time migration source for existing users.
        var legacySettings = Path.Combine(localAppData, "EasyConnect", "settings.json");
        if (!portable && directory == null && !File.Exists(FilePath) && File.Exists(legacySettings)) File.Copy(legacySettings, FilePath);
        Data = File.Exists(FilePath) ? Read(FilePath) : new();
        if (portable && !File.Exists(FilePath)) Save();
    }
    static AppData Read(string path)
    {
        var data = (Path.GetExtension(path).Equals(".ini", StringComparison.OrdinalIgnoreCase) ? IniConfiguration.Read(File.ReadAllText(path)) : JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), Json)) ?? throw new InvalidDataException("配置为空。");
        if (data.SchemaVersion is not (1 or 2)) throw new InvalidDataException("不支持此配置版本。原文件已保留。");
        if (data.Sessions == null || data.Credentials == null || data.HostKeys == null || data.Groups == null) throw new InvalidDataException("配置结构损坏。原文件已保留。");
        if (data.SchemaVersion == 2 && data.Credentials.Count > 0 && data.Vault == null) throw new InvalidDataException("密码库元数据缺失。原文件已保留。");
        if (data.Groups.Any(g => string.IsNullOrWhiteSpace(g) || g.Any(char.IsControl))) throw new InvalidDataException("分类数据损坏。原文件已保留。");
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
                if (IsPortable) { var bytes = System.Text.Encoding.UTF8.GetBytes(IniConfiguration.Write(Data)); f.Write(bytes); }
                else JsonSerializer.Serialize(f, Data, Json);
                f.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
    }
    public string GetPassword(Guid id)
    {
        lock (gate)
        {
            if (!Data.Credentials.TryGetValue(id.ToString(), out var encrypted)) return "";
            if (Data.Vault == null) throw new InvalidOperationException("请先设置主密码并迁移旧密码。");
            var inner = PasswordVault.Decrypt(encrypted, RequireVaultKey(), id.ToString());
            return Data.Vault.Version == 1 ? SecretProtection.Unprotect(inner) : inner;
        }
    }

    byte[] RequireVaultKey() => vaultKey ?? throw new InvalidOperationException("密码库尚未解锁。");

    public void UnlockVault(string password)
    {
        lock (gate)
        {
            var settings = Data.Vault ?? throw new InvalidOperationException("尚未设置主密码。");
            byte[] key = PasswordVault.Derive(password, settings);
            try
            {
                if (PasswordVault.Decrypt(settings.Verification, key, PasswordVault.VerificationContext) != PasswordVault.VerificationContext)
                    throw new CryptographicException("密码库校验失败。");
            }
            catch { CryptographicOperations.ZeroMemory(key); throw; }
            LockVault(); vaultKey = key;
        }
    }

    public void SetMasterPassword(string password)
    {
        if (password.Length < 8) throw new ArgumentException("主密码至少需要 8 个字符。");
        lock (gate)
        {
            var previousVault = Data.Vault;
            if (previousVault != null) _ = RequireVaultKey();
            var previousCredentials = Data.Credentials;
            int previousSchema = Data.SchemaVersion;
            var settings = new VaultSettings { Version = IsPortable ? 2 : 1, Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
            byte[] key = PasswordVault.Derive(password, settings);
            try
            {
                var credentials = new Dictionary<string, string>();
                foreach (var (id, encrypted) in previousCredentials)
                {
                    string inner = previousVault == null ? encrypted : PasswordVault.Decrypt(encrypted, RequireVaultKey(), id);
                    // Decode the original protection before changing the storage mode.
                    string plaintext = previousVault == null || previousVault.Version == 1 ? SecretProtection.Unprotect(inner) : inner;
                    credentials[id] = PasswordVault.Encrypt(settings.Version == 1 ? SecretProtection.Protect(plaintext) : plaintext, key, id);
                }
                settings.Verification = PasswordVault.Encrypt(PasswordVault.VerificationContext, key, PasswordVault.VerificationContext);
                Data.Vault = settings; Data.Credentials = credentials; Data.SchemaVersion = 2;
                try { Save(); }
                catch { Data.Vault = previousVault; Data.Credentials = previousCredentials; Data.SchemaVersion = previousSchema; throw; }
                LockVault(); vaultKey = key;
            }
            catch { CryptographicOperations.ZeroMemory(key); throw; }
        }
    }

    public void LockVault()
    {
        lock (gate) { if (vaultKey != null) CryptographicOperations.ZeroMemory(vaultKey); vaultKey = null; }
    }
    public void Dispose() => LockVault();

    public void AddGroup(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Any(char.IsControl) || name.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part != part.Trim() || part is "." or "..") || name.Contains('\\')) throw new ArgumentException("请输入有效的分类名称。");
        lock (gate)
        {
            if (name == "默认" || Data.Groups.Concat(Data.Sessions.Select(s => s.Group)).Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("该分类已存在。");
            Data.Groups.Add(name);
            try { Save(); } catch { Data.Groups.Remove(name); throw; }
        }
    }
    public void PutSession(Session session, string password)
    {
        lock (gate)
        {
            session.Validate();
            var encrypted = session.SavePassword && password.Length > 0
                ? PasswordVault.Encrypt(Data.Vault?.Version == 2 ? password : SecretProtection.Protect(password), RequireVaultKey(), session.Id.ToString()) : null;
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
    public void Export(string path) => File.WriteAllText(path, JsonSerializer.Serialize(new AppData { Groups = Data.Groups.ToList(), Sessions = Data.Sessions.Select(s => s with { SavePassword = false }).ToList() }, Json));
    public int Import(string path)
    {
        var imported = Read(path);
        Data.Groups = Data.Groups.Concat(imported.Groups).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var s in imported.Sessions) Data.Sessions.Add(s with { Id = Guid.NewGuid(), SavePassword = false });
        Save();
        return imported.Sessions.Count;
    }
}

