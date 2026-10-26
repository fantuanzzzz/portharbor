using System.Text.Json;
using System.Text.Json.Nodes;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void SessionRepositoryCompatibility(string root)
    {
        const string master = "fixture-master-password", secret = "fixture-session-secret";
        foreach (int version in new[] { 1, 2 })
        {
            string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"config-v{version}.json"));
            var expected = JsonSerializer.Deserialize<AppData>(source, Store.Json)!;
            var saved = expected.Sessions[0];
            var fixture = JsonNode.Parse(source)!;
            // Real DPAPI ciphertext is user-bound, so credentials are generated at runtime.
            if (version == 1)
                fixture["Credentials"]![saved.Id.ToString()] = new WindowsSecretProtector().Protect(secret);
            else
            {
                using var seed = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "fixture-vault-seed"), false));
                seed.SetMasterPassword(master); seed.PutSession(saved, secret);
                fixture["Vault"] = JsonSerializer.SerializeToNode(seed.Data.Vault, Store.Json);
                fixture["Credentials"] = JsonSerializer.SerializeToNode(seed.Data.Credentials, Store.Json);
            }
            string directory = Path.Combine(root, $"session-repository-v{version}");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, fixture.ToJsonString(Store.Json));
            byte[] original = File.ReadAllBytes(path);
            using var store = new Store(new WindowsSecretProtector(), TestPaths(directory, false));
            var service = new SessionService(store);
            Check(service.GetSessions().SequenceEqual(expected.Sessions) && store.Data.Groups.SequenceEqual(expected.Groups) && store.Data.HostKeys.SequenceEqual(expected.HostKeys)
                && store.Data.SchemaVersion == version && original.SequenceEqual(File.ReadAllBytes(path)), $"v{version} fixture loads unchanged through the application repository");
            if (version == 1)
            {
                Check(Throws<InvalidOperationException>(() => store.GetPassword(saved.Id)), "v1 fixture requires explicit credential migration");
                store.SetMasterPassword(master);
            }
            else store.UnlockVault(master);
            Check(store.GetPassword(saved.Id) == secret && store.Data.SchemaVersion == 2, $"v{version} credential remains associated with the original session after unlock or migration");

            var edited = saved with { Name = "Edited through Application", Group = "Work/Edited" };
            service.Save(edited, secret);
            var added = new Session { Name = "New shell", Protocol = Protocol.Shell, Executable = "cmd.exe", Arguments = "/Q" };
            service.Save(added, "unsaved-session-password");
            using (var reloaded = new Store(new WindowsSecretProtector(), TestPaths(directory, false)))
            {
                reloaded.UnlockVault(master);
                Check(reloaded.Data.Sessions.Count == 3 && reloaded.Data.Sessions.Single(s => s.Id == saved.Id) == edited
                    && reloaded.Data.Sessions.Single(s => s.Id == expected.Sessions[1].Id) == expected.Sessions[1] && reloaded.Data.Sessions.Single(s => s.Id == added.Id) == added
                    && reloaded.GetPassword(saved.Id) == secret && !reloaded.Data.Credentials.ContainsKey(added.Id.ToString()), $"v{version} create and edit preserve IDs, other sessions and credential association after restart");
                Check(reloaded.Data.Groups.SequenceEqual(expected.Groups) && reloaded.Data.HostKeys.SequenceEqual(expected.HostKeys)
                    && !File.ReadAllText(path).Contains(secret) && !File.ReadAllText(path).Contains("unsaved-session-password"), $"v{version} session changes preserve categories and host trust without persisting plaintext");
            }

            var previousSessions = service.GetSessions();
            string previousCredential = store.Data.Credentials[saved.Id.ToString()];
            byte[] previousFile = File.ReadAllBytes(path);
            Directory.CreateDirectory(path + ".tmp");
            bool Unchanged() => service.GetSessions().SequenceEqual(previousSessions) && store.Data.Credentials[saved.Id.ToString()] == previousCredential
                && store.GetPassword(saved.Id) == secret && previousFile.SequenceEqual(File.ReadAllBytes(path));
            Check(Throws<UnauthorizedAccessException>(() => service.Save(added with { Id = Guid.NewGuid() }, "")) && Unchanged(), $"v{version} failed create preserves in-memory and persisted sessions and credentials");
            Check(Throws<UnauthorizedAccessException>(() => service.Save(edited with { SavePassword = false }, "")) && Unchanged(), $"v{version} failed edit rolls back both session and credential changes");
            Check(Throws<UnauthorizedAccessException>(() => service.Delete(saved.Id)) && Unchanged(), $"v{version} failed delete rolls back both session and credential removal");
            Directory.Delete(path + ".tmp");
            service.Delete(saved.Id);
            using var deleted = new Store(new WindowsSecretProtector(), TestPaths(directory, false));
            Check(deleted.Data.Sessions.Count == 2 && deleted.Data.Sessions.All(s => s.Id != saved.Id) && !deleted.Data.Credentials.ContainsKey(saved.Id.ToString()),
                $"v{version} delete persists removal of the selected session and its credential");
        }

        string portableDirectory = Path.Combine(root, "application-portable");
        using var portable = new Store(new WindowsSecretProtector(), TestPaths(portableDirectory, true));
        portable.SetMasterPassword(master);
        var portableService = new SessionService(portable);
        var portableSession = new Session { Name = "Portable", Host = "portable.invalid", SavePassword = true };
        portableService.Save(portableSession, secret);
        using (var reloaded = new Store(new WindowsSecretProtector(), TestPaths(portableDirectory, true)))
        {
            reloaded.UnlockVault(master);
            Check(reloaded.Data.Vault?.Version == 2 && reloaded.Data.Sessions.Single() == portableSession && reloaded.GetPassword(portableSession.Id) == secret,
                "portable INI session and credential survive application save and restart");
        }
        portableService.Save(portableSession with { SavePassword = false }, secret);
        using var cleared = new Store(new WindowsSecretProtector(), TestPaths(portableDirectory, true));
        Check(!cleared.Data.Sessions.Single().SavePassword && cleared.Data.Credentials.Count == 0, "portable edit clears the credential when password saving is disabled");
    }
}
