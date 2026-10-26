using System.Security.Cryptography;

namespace PortHarbor;

internal sealed class StoredVault(ConfigurationState state, ISecretProtector secretProtector) : IVault, IDisposable
{
    byte[]? vaultKey;
    public bool HasMasterPassword => state.Data.Vault != null;
    public bool HasSavedCredentials => state.Data.Credentials.Count > 0;
    public bool IsUnlocked => vaultKey != null;
    public void Unlock(string password) => UnlockVault(password);
    public void Lock() => LockVault();
    internal string EncryptCredential(Guid id, string password)
    {
        var key = RequireVaultKey();
        return PasswordVault.Encrypt(state.Data.Vault?.Version == 2 ? password : secretProtector.Protect(password), key, id.ToString());
    }
    public string GetPassword(Guid id)
    {
        lock (state.Gate)
        {
            if (!state.Data.Credentials.TryGetValue(id.ToString(), out var encrypted)) return "";
            if (state.Data.Vault == null) throw new InvalidOperationException("请先设置主密码并迁移旧密码。");
            var inner = PasswordVault.Decrypt(encrypted, RequireVaultKey(), id.ToString());
            return state.Data.Vault.Version == 1 ? secretProtector.Unprotect(inner) : inner;
        }
    }

    byte[] RequireVaultKey() => vaultKey ?? throw new InvalidOperationException("密码库尚未解锁。");

    public void UnlockVault(string password)
    {
        lock (state.Gate)
        {
            var settings = state.Data.Vault ?? throw new InvalidOperationException("尚未设置主密码。");
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
        lock (state.Gate)
        {
            var previousVault = state.Data.Vault;
            if (previousVault != null) _ = RequireVaultKey();
            var previousCredentials = state.Data.Credentials;
            int previousSchema = state.Data.SchemaVersion;
            var settings = new VaultSettings { Version = state.IsPortable ? 2 : 1, Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
            byte[] key = PasswordVault.Derive(password, settings);
            try
            {
                var credentials = new Dictionary<string, string>();
                foreach (var (id, encrypted) in previousCredentials)
                {
                    string inner = previousVault == null ? encrypted : PasswordVault.Decrypt(encrypted, RequireVaultKey(), id);
                    // Decode the original protection before changing the storage mode.
                    string plaintext = previousVault == null || previousVault.Version == 1 ? secretProtector.Unprotect(inner) : inner;
                    credentials[id] = PasswordVault.Encrypt(settings.Version == 1 ? secretProtector.Protect(plaintext) : plaintext, key, id);
                }
                settings.Verification = PasswordVault.Encrypt(PasswordVault.VerificationContext, key, PasswordVault.VerificationContext);
                state.Data.Vault = settings; state.Data.Credentials = credentials; state.Data.SchemaVersion = 2;
                try { state.Save(); }
                catch { state.Data.Vault = previousVault; state.Data.Credentials = previousCredentials; state.Data.SchemaVersion = previousSchema; throw; }
                LockVault(); vaultKey = key;
            }
            catch { CryptographicOperations.ZeroMemory(key); throw; }
        }
    }

    public void LockVault()
    {
        lock (state.Gate) { if (vaultKey != null) CryptographicOperations.ZeroMemory(vaultKey); vaultKey = null; }
    }
    public void Dispose() => LockVault();

}
