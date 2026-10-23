using System.Security.Cryptography;
using System.Text.Json;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void VaultAndGroups(string root)
    {
        using var store = new Store(Path.Combine(root, "vault"));
        var session = new Session { Host = "localhost", SavePassword = true };
        Check(Throws<InvalidOperationException>(() => store.PutSession(session, "secret")), "saving a password requires master password setup");
        Check(Throws<ArgumentException>(() => store.SetMasterPassword("short")), "short master password is rejected");
        store.AddGroup(" 空分类 ");
        Check(Throws<ArgumentException>(() => store.AddGroup("空分类")), "duplicate category is rejected");
        using (var reloaded = new Store(store.DirectoryPath)) Check(reloaded.Data.Groups.Contains("空分类") && reloaded.Data.Sessions.Count == 0, "empty category persists independently of sessions");
        store.SetMasterPassword("master-password-1");
        store.PutSession(session, "会话密码-one");
        var second = session with { Id = Guid.NewGuid() };
        store.PutSession(second, "session-two");
        string text = File.ReadAllText(store.FilePath);
        Check(!text.Contains("master-password-1") && !text.Contains("会话密码-one"), "master password and session plaintext are absent from configuration");
        store.LockVault();
        Check(Throws<InvalidOperationException>(() => store.SetMasterPassword("locked-replacement")), "locked vault cannot change master password");
        Check(Throws<InvalidOperationException>(() => store.GetPassword(session.Id)), "locked vault refuses credential access");
        Check(Throws<CryptographicException>(() => store.UnlockVault("wrong-password")) && !store.IsVaultUnlocked, "wrong master password leaves vault locked");
        store.UnlockVault("master-password-1");
        Check(store.GetPassword(session.Id) == "会话密码-one", "correct master password unlocks credential");
        var original = store.Data.Credentials[session.Id.ToString()];
        store.Data.Credentials[session.Id.ToString()] = store.Data.Credentials[second.Id.ToString()];
        Check(Throws<CryptographicException>(() => store.GetPassword(session.Id)), "credential ciphertext cannot be substituted across sessions");
        store.Data.Credentials[session.Id.ToString()] = original;
        var bytes = Convert.FromBase64String(original); bytes[^1] ^= 1;
        store.Data.Credentials[session.Id.ToString()] = Convert.ToBase64String(bytes);
        Check(Throws<CryptographicException>(() => store.GetPassword(session.Id)), "modified credential authentication tag is rejected");
        store.Data.Credentials[session.Id.ToString()] = original;
        store.SetMasterPassword("master-password-2");
        using (var restarted = new Store(store.DirectoryPath))
        {
            Check(!restarted.IsVaultUnlocked && Throws<CryptographicException>(() => restarted.UnlockVault("master-password-1")), "restart locks vault and rejects previous master password after change");
            restarted.UnlockVault("master-password-2");
            Check(restarted.GetPassword(session.Id) == "会话密码-one" && restarted.GetPassword(second.Id) == "session-two", "master password change preserves all credentials");
        }
        string export = Path.Combine(root, "vault-export.json"); store.Export(export);
        var exported = JsonSerializer.Deserialize<AppData>(File.ReadAllText(export), Store.Json)!;
        Check(exported.Vault == null && exported.Credentials.Count == 0 && exported.Sessions.All(s => !s.SavePassword), "export omits vault metadata and credentials");
        using (var imported = new Store(Path.Combine(root, "import-groups")))
        {
            imported.Import(export);
            Check(imported.Data.Groups.Contains("空分类"), "export and import preserve empty categories");
        }

        string legacyPath = Path.Combine(root, "legacy-vault"); Directory.CreateDirectory(legacyPath);
        var legacy = new AppData { SchemaVersion = 1, Sessions = [session], Credentials = new() { [session.Id.ToString()] = SecretProtection.Protect("legacy-secret") } };
        File.WriteAllText(Path.Combine(legacyPath, "settings.json"), JsonSerializer.Serialize(legacy, Store.Json));
        using var migrated = new Store(legacyPath);
        Check(Throws<InvalidOperationException>(() => migrated.GetPassword(session.Id)), "legacy credentials require migration before UI access");
        migrated.SetMasterPassword("migration-master");
        Check(migrated.Data.SchemaVersion == 2 && migrated.GetPassword(session.Id) == "legacy-secret", "legacy DPAPI credential migrates without data loss");

        var previous = migrated.Data.Credentials[session.Id.ToString()];
        // A directory at the temporary-file path forces persistence failure before replacement.
        Directory.CreateDirectory(migrated.FilePath + ".tmp");
        Check(Throws<UnauthorizedAccessException>(() => migrated.SetMasterPassword("replacement-master")) && migrated.Data.Credentials[session.Id.ToString()] == previous && migrated.GetPassword(session.Id) == "legacy-secret", "failed master password change rolls back in-memory vault and preserves old key");
        using var disk = new Store(legacyPath); disk.UnlockVault("migration-master");
        Check(disk.GetPassword(session.Id) == "legacy-secret", "failed master password change preserves persisted vault");
    }
}
