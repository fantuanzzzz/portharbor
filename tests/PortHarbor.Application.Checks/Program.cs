using System.Text.Json;
using PortHarbor;

var results = new List<string>();
string? failure = null;
string transferRoot = Path.Combine(Path.GetTempPath(), "PortHarbor-application-check-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(transferRoot);
    var repository = new MemorySessionRepository();
    var service = new SessionService(repository);
    var first = new Session { Name = "Linux", Host = "server.invalid", SavePassword = true };
    service.Save(first, "session-secret");
    Check(repository.Sessions.Single().Id == first.Id && repository.LastPassword == "session-secret", "create preserves session ID and forwards its credential to the repository");

    var second = new Session { Name = "Switch", Host = "switch.invalid", Protocol = Protocol.Telnet, Port = 23 };
    service.Save(second, "not-for-storage");
    Check(repository.LastPassword == "", "disabled password saving discards the supplied credential before persistence");
    service.Save(first with { Name = "Renamed", Group = "Work/Linux" }, "replacement-secret");
    Check(repository.Sessions.Count == 2 && repository.Sessions.Single(s => s.Id == first.Id).Name == "Renamed" && repository.Sessions.Single(s => s.Id == second.Id) == second,
        "edit replaces the existing ID and preserves other sessions");

    int writes = repository.Writes;
    bool rejected = false;
    try { service.Save(first with { Host = "invalid host" }, "unused"); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected && repository.Writes == writes, "invalid session is rejected before repository writes");

    var listed = service.GetSessions();
    listed.Single(s => s.Id == first.Id).Host = "unsaved.invalid";
    Check(service.GetSessions().Single(s => s.Id == first.Id).Host == "server.invalid", "editing a listed session cannot mutate repository state");
    second.Name = "Unsaved change";
    Check(service.GetSessions().Single(s => s.Id == second.Id).Name == "Switch", "changing the caller record after save cannot mutate persisted state");

    service.Save(first, "");
    Check(repository.LastPassword == "", "empty password retains the existing clear-credential contract");
    service.Save(first with { SavePassword = false }, "discarded");
    Check(!repository.Sessions.Single(s => s.Id == first.Id).SavePassword && repository.LastPassword == "", "disabling password saving updates the same session and clears its credential request");

    var error = new IOException("simulated repository failure");
    repository.Failure = error;
    Exception? observed = null;
    try { service.Save(first, "unused"); } catch (IOException e) { observed = e; }
    Check(ReferenceEquals(observed, error), "save failure propagates to the caller without reporting success");
    observed = null;
    try { service.Delete(first.Id); } catch (IOException e) { observed = e; }
    Check(ReferenceEquals(observed, error), "delete failure propagates to the caller without reporting success");
    repository.Failure = null;
    service.Delete(first.Id);
    service.Delete(first.Id);
    Check(service.GetSessions().Single().Id == second.Id, "delete targets the selected ID and repeated deletion preserves other sessions");

    var configRepository = new MemorySessionRepository();
    var vault = new MemoryVault();
    var hostKeys = new MemoryHostKeyTrust();
    var configuration = new ConfigurationService(configRepository, vault, hostKeys);
    configuration.AddGroup("Work/Linux");
    configuration.SaveSession(first, "session-secret");
    Check(configuration.GetSessions().Single().Id == first.Id && configuration.GetGroups().SequenceEqual(["Work/Linux"]), "configuration service routes session and group access through the repository");
    configuration.Export("export-fixture.json");
    Check(configRepository.LastExportPath == "export-fixture.json" && configuration.Import("import-fixture.json") == 2
        && configRepository.LastImportPath == "import-fixture.json", "configuration service delegates import and export through the repository");
    vault.Unlock("application-master");
    configuration.SetMasterPassword("replacement-master");
    Check(configuration.HasMasterPassword && configuration.HasSavedCredentials && configuration.IsVaultUnlocked
        && configuration.GetPassword(first.Id) == "vault-secret", "configuration service routes vault state, unlock and credential access through the vault port");
    configuration.LockVault();
    Check(!configuration.IsVaultUnlocked && vault.LockCount == 1, "configuration service locks the vault through its port");
    Check(configuration.CheckHostKey("switch.invalid", 22, "SHA256:test", _ => true)
        && hostKeys.Host == "switch.invalid" && hostKeys.Fingerprint == "SHA256:test", "configuration service delegates host key trust decisions");
    configuration.DeleteSession(first.Id);
    Check(configRepository.DeletedId == first.Id, "configuration service delegates session deletion through the repository");
    await PortHarbor.Checks.FileTransferPolicyChecks.RunAsync(transferRoot, Check);
    await PortHarbor.Checks.TerminalSessionChecks.RunAsync(Check);
    PortHarbor.Checks.RemoteDesktopChecks.Run(Check);
    PortHarbor.Checks.PlatformCapabilityChecks.Run(Check);
    PortHarbor.Checks.ConfigurationTransferChecks.Run(Check);
    PortHarbor.Checks.DiagnosticMessageChecks.Run(Check);
    PortHarbor.Checks.SessionLibraryChecks.Run(Check);
    PortHarbor.Checks.SessionActivityChecks.Run(Check);
    PortHarbor.Checks.SessionValidationChecks.Run(Check);
    await PortHarbor.Checks.TransferQueueChecks.RunAsync(Check);
    PortHarbor.Checks.WorkspacePlacementChecks.Run(Check);
    Console.WriteLine($"{results.Count} checks passed");
    return 0;
}
catch (Exception e)
{
    failure = e.ToString(); Console.Error.WriteLine(e); return 1;
}
finally
{
    if (Directory.Exists(transferRoot)) Directory.Delete(transferRoot, true);
    int index = Array.IndexOf(args, "--results");
    if (index >= 0 && index + 1 < args.Length)
    {
        string path = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { passed = results.Count, results, failure, date = DateTimeOffset.Now }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    results.Add(name); Console.WriteLine("PASS: " + name);
}

sealed class MemorySessionRepository : IConfigurationRepository
{
    public List<Session> Sessions { get; } = [];
    public List<string> Groups { get; } = [];
    public string LastPassword { get; private set; } = "";
    public int Writes { get; private set; }
    public IOException? Failure { get; set; }
    public string? LastExportPath { get; private set; }
    public string? LastImportPath { get; private set; }
    public Guid? DeletedId { get; private set; }
    public IReadOnlyList<Session> GetSessions() => Sessions;
    public IReadOnlyList<string> GetGroups() => Groups;
    public void AddGroup(string name) => Groups.Add(name);
    public SessionConfiguration ReadSessionFile(string path)
    {
        LastImportPath = path;
        return new([], [new Session { Host = "import-one.invalid" }, new Session { Host = "import-two.invalid" }]);
    }
    public void WriteSessionFile(string path, SessionConfiguration configuration) => LastExportPath = path;
    public SessionConfiguration GetSessionConfiguration() => new(Groups.ToArray(), Sessions.Select(s => s with { }).ToArray());
    public void UpdateSessionConfiguration(Func<SessionConfiguration, SessionConfiguration> change)
    {
        if (Failure != null) throw Failure;
        var updated = change(GetSessionConfiguration());
        Groups.Clear(); Groups.AddRange(updated.Groups);
        Sessions.Clear(); Sessions.AddRange(updated.Sessions);
    }
    public void Save(Session session, string password)
    {
        if (Failure != null) throw Failure;
        Writes++; LastPassword = password;
        Sessions.RemoveAll(s => s.Id == session.Id); Sessions.Add(session);
    }
    public void Delete(Guid id)
    {
        if (Failure != null) throw Failure;
        DeletedId = id;
        Sessions.RemoveAll(s => s.Id == id);
    }
}

sealed class MemoryVault : IVault
{
    public bool HasMasterPassword { get; private set; }
    public bool HasSavedCredentials => true;
    public bool IsUnlocked { get; private set; }
    public int LockCount { get; private set; }
    public string GetPassword(Guid sessionId) => IsUnlocked ? "vault-secret" : throw new InvalidOperationException();
    public void Unlock(string password) => IsUnlocked = true;
    public void SetMasterPassword(string password) { HasMasterPassword = true; IsUnlocked = true; }
    public void Lock() { IsUnlocked = false; LockCount++; }
}

sealed class MemoryHostKeyTrust : IHostKeyTrust
{
    public string? Host { get; private set; }
    public string? Fingerprint { get; private set; }
    public bool CheckHostKey(string host, int port, string fingerprint, Func<string, bool> confirm)
    {
        Host = host; Fingerprint = fingerprint;
        return confirm(fingerprint);
    }
}
