namespace PortHarbor;

public interface IConfigurationRepository : ISessionRepository, IConfigurationTransfer
{
    IReadOnlyList<string> GetGroups();
    void AddGroup(string name);
}

public interface IVault
{
    bool HasMasterPassword { get; }
    bool HasSavedCredentials { get; }
    bool IsUnlocked { get; }
    string GetPassword(Guid sessionId);
    void Unlock(string password);
    void SetMasterPassword(string password);
    void Lock();
}

public interface IHostKeyTrust
{
    bool CheckHostKey(string host, int port, string fingerprint, Func<string, bool> confirm);
}

public sealed class ConfigurationService : ISessionRepository, IHostKeyTrust
{
    readonly IConfigurationRepository repository;
    readonly IVault vault;
    readonly IHostKeyTrust hostKeyTrust;
    readonly SessionService sessions;
    readonly ConfigurationTransferService transfers;

    public ConfigurationService(IConfigurationRepository repository, IVault vault, IHostKeyTrust hostKeyTrust)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.vault = vault ?? throw new ArgumentNullException(nameof(vault));
        this.hostKeyTrust = hostKeyTrust ?? throw new ArgumentNullException(nameof(hostKeyTrust));
        sessions = new SessionService(repository);
        transfers = new ConfigurationTransferService(repository);
    }

    public IReadOnlyList<Session> GetSessions() => sessions.GetSessions();
    public IReadOnlyList<string> GetGroups() => repository.GetGroups();
    public void SaveSession(Session session, string password) => sessions.Save(session, password);
    public void DeleteSession(Guid id) => sessions.Delete(id);
    public void AddGroup(string name) => repository.AddGroup(name);
    public void Export(string path) => transfers.Export(path);
    public int Import(string path) => transfers.Import(path);
    public bool HasMasterPassword => vault.HasMasterPassword;
    public bool HasSavedCredentials => vault.HasSavedCredentials;
    public bool IsVaultUnlocked => vault.IsUnlocked;
    public string GetPassword(Guid sessionId) => vault.GetPassword(sessionId);
    public void UnlockVault(string password) => vault.Unlock(password);
    public void SetMasterPassword(string password) => vault.SetMasterPassword(password);
    public void LockVault() => vault.Lock();
    public bool CheckHostKey(string host, int port, string fingerprint, Func<string, bool> confirm) => hostKeyTrust.CheckHostKey(host, port, fingerprint, confirm);

    public void Save(Session session, string password) => sessions.Save(session, password);
    public void Delete(Guid id) => sessions.Delete(id);
}
