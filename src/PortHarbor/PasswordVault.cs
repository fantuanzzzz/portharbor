using System.Security.Cryptography;
using System.Text;

namespace PortHarbor;

public sealed class VaultSettings
{
    public int Version { get; set; } = 1;
    public string Salt { get; set; } = "";
    public string Verification { get; set; } = "";
}

// Version 1 adds Windows DPAPI; version 2 is portable with master-password protection.
internal static class PasswordVault
{
    const int Iterations = 600_000;
    internal static byte[] Derive(string password, VaultSettings settings)
    {
        if (settings.Version is not (1 or 2)) throw new InvalidDataException("不支持的密码库版本。");
        var salt = Convert.FromBase64String(settings.Salt);
        if (salt.Length != 32) throw new InvalidDataException("密码库盐值损坏。");
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
    }

    internal static string Encrypt(string value, byte[] key, string context)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(value);
        byte[] envelope = new byte[12 + 16 + plaintext.Length];
        RandomNumberGenerator.Fill(envelope.AsSpan(0, 12));
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(envelope.AsSpan(0, 12), plaintext, envelope.AsSpan(28), envelope.AsSpan(12, 16), Encoding.UTF8.GetBytes(context));
            return Convert.ToBase64String(envelope);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal static string Decrypt(string value, byte[] key, string context)
    {
        var envelope = Convert.FromBase64String(value);
        if (envelope.Length < 28) throw new CryptographicException("密码库数据损坏。");
        byte[] plaintext = new byte[envelope.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.AsSpan(0, 12), envelope.AsSpan(28), envelope.AsSpan(12, 16), plaintext, Encoding.UTF8.GetBytes(context));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal const string VerificationContext = "PortHarbor.vault.v1";
}
