using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void PlatformServiceBoundaries(string root)
    {
        var platformAssembly = typeof(WindowsSecretProtector).Assembly;
        Check(platformAssembly == typeof(WindowsRemoteDesktopLauncher).Assembly && platformAssembly == typeof(ConPty).Assembly
            && typeof(RdpLauncher).Assembly == typeof(SessionService).Assembly
            && typeof(Store).Assembly.GetReferencedAssemblies().All(a => a.Name != "PortHarbor.Platform.Windows"),
            "DPAPI and mstsc live in the Windows platform while storage and RDP use cases depend on application ports");

        var events = new List<ConnectionDiagnostic>();
        var session = new Session { Protocol = Protocol.RDP, Host = "private-host", Username = "private-user" };
        string directory = Path.Combine(root, "rdp-platform"); Directory.CreateDirectory(directory);
        var paths = new AppDataPaths(false, Path.GetFullPath(directory), Path.Combine(Path.GetFullPath(directory), "settings.json"), null, Path.GetFullPath(directory));
        bool started = false;
        var launcher = new RdpLauncher(new WindowsRemoteDesktopLauncher(_ => { started = true; throw new Win32Exception(2); }), paths);
        var missingPaths = paths with { TemporaryDirectory = Path.Combine(directory, "missing") };
        var missingLauncher = new RdpLauncher(new WindowsRemoteDesktopLauncher(_ => { started = true; throw new Win32Exception(2); }), missingPaths);
        Check(Throws<DirectoryNotFoundException>(() => missingLauncher.Launch(session, events.Add))
            && !started && events.Last().Stage == ConnectionStage.Writing,
            "Windows RDP configuration write failure prevents starting the external client");
        events.Clear();
        Check(Throws<Win32Exception>(() => launcher.Launch(session, events.Add)) && started
            && !Directory.EnumerateFiles(directory).Any() && events.Last().Stage == ConnectionStage.LaunchingClient
            && events.Last().ErrorCode == ConnectionErrorCode.PlatformUnavailable,
            "Windows RDP startup failure removes the temporary configuration and preserves diagnostic stage");

        var unavailable = new UnavailableSecretProtector();
        var saved = new Session { Host = "vault.invalid", SavePassword = true };
        using var portable = new Store(unavailable, TestPaths(Path.Combine(root, "portable-without-dpapi"), true));
        portable.SetMasterPassword("portable-master"); portable.PutSession(saved, "portable-secret");
        portable.SetMasterPassword("portable-new-master"); portable.LockVault(); portable.UnlockVault("portable-new-master");
        Check(portable.GetPassword(saved.Id) == "portable-secret" && unavailable.Calls == 0 && portable.Data.Vault?.Version == 2,
            "portable vault create, read, rekey and unlock never call platform secret protection");
        using var installed = new Store(unavailable, TestPaths(Path.Combine(root, "installed-no-fallback"), false));
        Check(Throws<InvalidOperationException>(() => installed.PutSession(saved, "secret")) && unavailable.Calls == 0,
            "locked vault is rejected before invoking platform secret protection");
        installed.SetMasterPassword("installed-master");
        byte[] original = File.ReadAllBytes(installed.FilePath);
        Check(Throws<PlatformNotSupportedException>(() => installed.PutSession(saved, "secret"))
            && installed.GetSessions().Count == 0 && installed.Data.Credentials.Count == 0 && installed.Data.Vault?.Version == 1
            && original.SequenceEqual(File.ReadAllBytes(installed.FilePath)),
            "unavailable platform protection cannot downgrade installed vault encryption or change stored sessions");
    }

    static async Task WindowsServices(string root)
    {
        var protector = new WindowsSecretProtector();
        foreach (string secret in new[] { "", "凭据🔑-roundtrip" })
            Check(new WindowsSecretProtector().Unprotect(protector.Protect(secret)) == secret,
                "Windows user-scoped DPAPI roundtrip preserves " + (secret.Length == 0 ? "empty" : "Unicode") + " secrets across adapter instances");
        var damaged = Convert.FromBase64String(protector.Protect("secret")); damaged[^1] ^= 1;
        Check(Throws<Win32Exception>(() => protector.Unprotect(Convert.ToBase64String(damaged))), "Windows DPAPI rejects modified ciphertext");

        const string oldMaster = "migration-old-master", newMaster = "migration-new-master", secretValue = "mode-switch-secret";
        var session = new Session { Host = "migration.invalid", SavePassword = true };
        using var seed = new Store(protector, TestPaths(Path.Combine(root, "mode-seed"), false));
        seed.SetMasterPassword(oldMaster); seed.PutSession(session, secretValue);
        using var portable = new Store(protector, TestPaths(Path.Combine(root, "mode-portable"), true));
        portable.Data.Vault = seed.Data.Vault; portable.Data.Credentials = new(seed.Data.Credentials); portable.Data.Sessions = [session]; portable.Save();
        portable.UnlockVault(oldMaster); portable.SetMasterPassword(newMaster);
        using (var reloaded = new Store(new UnavailableSecretProtector(), TestPaths(portable.DirectoryPath, true)))
        {
            reloaded.UnlockVault(newMaster);
            Check(reloaded.Data.Vault?.Version == 2 && reloaded.GetPassword(session.Id) == secretValue,
                "installed DPAPI vault converts to portable protection on explicit master password change and restarts without DPAPI");
        }
        using var installed = new Store(protector, TestPaths(Path.Combine(root, "mode-installed"), false));
        installed.Data.Vault = portable.Data.Vault; installed.Data.Credentials = new(portable.Data.Credentials); installed.Data.Sessions = [session]; installed.Save();
        installed.UnlockVault(newMaster); installed.SetMasterPassword(oldMaster);
        using (var reloaded = new Store(protector, TestPaths(installed.DirectoryPath, false)))
        {
            reloaded.UnlockVault(oldMaster);
            Check(reloaded.Data.Vault?.Version == 1 && reloaded.GetPassword(session.Id) == secretValue,
                "portable vault converts back to installed DPAPI protection on explicit master password change without losing credentials");
        }
        using (var blocked = new Store(new UnavailableSecretProtector(), TestPaths(installed.DirectoryPath, false)))
        {
            blocked.UnlockVault(oldMaster);
            byte[] previous = File.ReadAllBytes(blocked.FilePath);
            Check(Throws<PlatformNotSupportedException>(() => blocked.GetPassword(session.Id))
                && Throws<PlatformNotSupportedException>(() => blocked.SetMasterPassword(newMaster))
                && previous.SequenceEqual(File.ReadAllBytes(blocked.FilePath)) && blocked.IsVaultUnlocked,
                "DPAPI decryption failure during read or rekey preserves the installed vault file and active key");
        }

        string directory = Path.Combine(root, "rdp-process space"); Directory.CreateDirectory(directory);
        string? path = null; bool correctArguments = false;
        var paths = new AppDataPaths(false, Path.GetFullPath(directory), Path.Combine(Path.GetFullPath(directory), "settings.json"), null, Path.GetFullPath(directory));
        var launcher = new RdpLauncher(new WindowsRemoteDesktopLauncher(info =>
        {
            path = info.ArgumentList.Single();
            var bytes = File.ReadAllBytes(path);
            correctArguments = info.FileName == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe")
                && !info.UseShellExecute && info.Arguments == "" && bytes.Take(2).SequenceEqual(Encoding.Unicode.GetPreamble())
                && File.ReadAllText(path) == RdpLauncher.Configuration(session with { Protocol = Protocol.RDP });
            // Exercise process ownership/exit cleanup without opening an actual remote desktop.
            var processInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
                { UseShellExecute = false, CreateNoWindow = true };
            processInfo.ArgumentList.Add("/c"); processInfo.ArgumentList.Add("exit 0");
            return Process.Start(processInfo)!;
        }), paths);
        var events = new List<ConnectionDiagnostic>();
        launcher.Launch(session with { Protocol = Protocol.RDP }, events.Add);
        Check(correctArguments && events.Last().State == ConnectionState.Connected
            && DiagnosticPresenter.Status(events.Last()) == "已启动 Windows 远程桌面客户端",
            "Windows RDP uses a single safe file argument, UTF-16 configuration and client-started status");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (File.Exists(path)) await Task.Delay(20, deadline.Token);
        Check(!Directory.EnumerateFiles(directory).Any(), "Windows RDP removes its temporary configuration when the owned client process exits");

        int processId = 0;
        await using var observing = new WindowsRemoteDesktopLauncher(_ =>
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
                { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("/c"); info.ArgumentList.Add("ping -n 20 127.0.0.1 > nul");
            var child = Process.Start(info)!; processId = child.Id; return child;
        });
        observing.Launch(RdpLauncher.Configuration(session with { Protocol = Protocol.RDP }), directory, _ => { });
        try
        {
            await observing.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            using var external = Process.GetProcessById(processId);
            Check(observing.CleanupCompletion.IsCompletedSuccessfully && !external.HasExited && !Directory.EnumerateFiles(directory).Any(),
                "disposing the RDP launcher awaits watcher cleanup and removes temporary files without terminating the external client");
            Check(Throws<ObjectDisposedException>(() => observing.Launch("", directory, _ => { })),
                "a disposed RDP launcher refuses new client launches");
        }
        finally
        {
            // Only the test owns this stand-in process; the application never kills mstsc.
            using var child = Process.GetProcessById(processId);
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        }
    }

    sealed class UnavailableSecretProtector : ISecretProtector
    {
        internal int Calls;
        public string Protect(string value) { Calls++; throw new PlatformNotSupportedException("fixture unavailable"); }
        public string Unprotect(string value) { Calls++; throw new PlatformNotSupportedException("fixture unavailable"); }
    }
}
