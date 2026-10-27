using System.Text.Json;

namespace PortHarbor;

public sealed class Store : IDisposable, IConfigurationRepository, IVault, IHostKeyTrust
{
    public static readonly JsonSerializerOptions Json = ConfigurationFileStorage.Json;
    readonly ConfigurationState state;
    readonly StoredVault vault;
    readonly StoredHostKeyTrust hostKeyTrust;
    object gate => state.Gate;
    public string DirectoryPath => state.Storage.DirectoryPath;
    public bool IsPortable => state.IsPortable;
    public string FilePath => state.Storage.FilePath;
    public AppData Data => state.Data;
    public IAppDataPaths Paths { get; }
    public bool HasMasterPassword => vault.HasMasterPassword;
    public bool HasSavedCredentials => vault.HasSavedCredentials;
    public bool IsVaultUnlocked => vault.IsUnlocked;
    bool IVault.IsUnlocked => vault.IsUnlocked;
    void IVault.Unlock(string password) => vault.Unlock(password);
    void IVault.Lock() => vault.Lock();
    public Store(ISecretProtector secretProtector, IAppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(secretProtector);
        ArgumentNullException.ThrowIfNull(paths);
        if (Path.GetFullPath(paths.ConfigurationDirectory) != paths.ConfigurationDirectory
            || Path.GetFullPath(paths.ConfigurationFile) != paths.ConfigurationFile
            || Path.GetFullPath(paths.TemporaryDirectory) != paths.TemporaryDirectory
            || Path.GetDirectoryName(paths.ConfigurationFile) != paths.ConfigurationDirectory)
            throw new ArgumentException("Resolved application paths must be absolute and keep the configuration file inside its directory.", nameof(paths));
        Paths = new AppDataPaths(paths.IsPortable, paths.ConfigurationDirectory, paths.ConfigurationFile,
            paths.LegacyConfigurationFile, paths.TemporaryDirectory);
        state = new ConfigurationState(new ConfigurationFileStorage(Paths));
        vault = new StoredVault(state, secretProtector);
        hostKeyTrust = new StoredHostKeyTrust(state);
    }
    public void Save() => state.Save();
    public string GetPassword(Guid id) => vault.GetPassword(id);
    public void UnlockVault(string password) => vault.Unlock(password);
    public void SetMasterPassword(string password) => vault.SetMasterPassword(password);
    public void LockVault() => vault.Lock();
    public void Dispose() => vault.Dispose();

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
    public IReadOnlyList<Session> GetSessions()
    {
        lock (gate) return Data.Sessions.Select(s => s with { }).ToArray();
    }
    public IReadOnlyList<string> GetGroups()
    {
        lock (gate) return Data.Groups.ToArray();
    }
    public void PutSession(Session session, string password)
    {
        lock (gate)
        {
            session = session with { };
            session.Validate();
            var encrypted = session.SavePassword && password.Length > 0 ? vault.EncryptCredential(session.Id, password) : null;
            var sessions = Data.Sessions.Where(s => s.Id != session.Id).Append(session).ToList();
            var credentials = new Dictionary<string, string>(Data.Credentials);
            if (encrypted != null) credentials[session.Id.ToString()] = encrypted;
            else credentials.Remove(session.Id.ToString());
            SaveSessionChanges(sessions, credentials);
        }
    }
    void ISessionRepository.Save(Session session, string password) => PutSession(session, password);
    public void Delete(Guid id)
    {
        lock (gate)
        {
            var sessions = Data.Sessions.Where(s => s.Id != id).ToList();
            var credentials = new Dictionary<string, string>(Data.Credentials);
            credentials.Remove(id.ToString());
            SaveSessionChanges(sessions, credentials);
        }
    }
    void SaveSessionChanges(List<Session> sessions, Dictionary<string, string> credentials)
    {
        var previousSessions = Data.Sessions;
        var previousCredentials = Data.Credentials;
        Data.Sessions = sessions; Data.Credentials = credentials;
        try { Save(); }
        catch { Data.Sessions = previousSessions; Data.Credentials = previousCredentials; throw; }
    }
    public bool CheckHostKey(string host, int port, string fingerprint, Func<string, bool> confirm)
        => hostKeyTrust.CheckHostKey(host, port, fingerprint, confirm);
    // Preserve existing callers while all transfer policy lives in Application.
    public void Export(string path) => new ConfigurationTransferService(this).Export(path);
    public int Import(string path) => new ConfigurationTransferService(this).Import(path);
    SessionConfiguration IConfigurationTransfer.ReadSessionFile(string path)
    {
        var data = ConfigurationFileStorage.Read(path);
        return new SessionConfiguration(data.Groups.ToArray(), data.Sessions.Select(s => s with { }).ToArray());
    }
    void IConfigurationTransfer.WriteSessionFile(string path, SessionConfiguration configuration)
        => ConfigurationFileStorage.WriteSessionFile(path, configuration);
    SessionConfiguration IConfigurationTransfer.GetSessionConfiguration()
    {
        lock (gate) return SessionSnapshot();
    }
    SessionConfiguration SessionSnapshot() => new(Data.Groups.ToArray(), Data.Sessions.Select(s => s with { }).ToArray());
    void IConfigurationTransfer.UpdateSessionConfiguration(Func<SessionConfiguration, SessionConfiguration> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (gate)
        {
            var updated = change(SessionSnapshot());
            var groups = updated.Groups.ToList();
            var sessions = updated.Sessions.Select(s => s with { }).ToList();
            var previousGroups = Data.Groups;
            var previousSessions = Data.Sessions;
            Data.Groups = groups;
            Data.Sessions = sessions;
            try { Save(); }
            catch { Data.Groups = previousGroups; Data.Sessions = previousSessions; throw; }
        }
    }
}
