using System.Text.Json;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void ConfigurationTransferStorage(string root)
    {
        foreach (bool portable in new[] { false, true })
        {
            string mode = portable ? "INI" : "JSON";
            string directory = Path.Combine(root, "transfer-policy-" + mode);
            using var store = new Store(new WindowsSecretProtector(), TestPaths(directory, portable));
            var service = new ConfigurationService(store, store, store);
            var port = (IConfigurationTransfer)store;
            store.SetMasterPassword("transfer-policy-master");
            var existing = new Session { Host = "existing-transfer.invalid", SavePassword = true };
            service.SaveSession(existing, "retained-session-secret"); service.AddGroup("Work/Empty");
            service.CheckHostKey(existing.Host, 22, "SHA256:retained-trust", _ => true);
            string ciphertext = store.Data.Credentials[existing.Id.ToString()];
            var vault = store.Data.Vault;
            foreach (string format in new[] { "v1", "v2", "ini" })
            {
                var incoming = existing with { Name = "Imported " + format, Host = "incoming-transfer.invalid" };
                var data = new AppData
                {
                    SchemaVersion = format == "v1" ? 1 : 2, Vault = format == "v1" ? null : vault,
                    Groups = ["work/empty", "Import/" + format], Sessions = [incoming],
                    Credentials = new() { [incoming.Id.ToString()] = "untrusted-ciphertext" },
                    HostKeys = new() { ["[incoming-transfer.invalid]:22"] = "SHA256:untrusted" }
                };
                string file = Path.Combine(root, "transfer-" + mode + "-" + format + (format == "ini" ? ".ini" : ".json"));
                File.WriteAllText(file, format == "ini" ? IniConfiguration.Write(data) : JsonSerializer.Serialize(data, Store.Json));
                byte[] source = File.ReadAllBytes(file);
                Check(service.Import(file) == 1 && store.GetSessions().Last().Id != existing.Id
                    && !store.GetSessions().Last().SavePassword && store.GetGroups().Count(g => g.Equals("Work/Empty", StringComparison.OrdinalIgnoreCase)) == 1
                    && source.SequenceEqual(File.ReadAllBytes(file)), mode + " storage imports " + format + " without mutating its source, reusing identity or duplicating categories");
            }
            Check(store.IsPortable == portable && Path.GetExtension(store.FilePath) == (portable ? ".ini" : ".json")
                && store.Data.Credentials.Single().Value == ciphertext && ReferenceEquals(store.Data.Vault, vault)
                && store.GetPassword(existing.Id) == "retained-session-secret" && store.Data.HostKeys.Count == 1
                && store.GetSessions().Single(s => s.Id == existing.Id).SavePassword,
                mode + " imports preserve existing encrypted credentials, active vault and independently trusted hosts");
            var snapshot = port.GetSessionConfiguration(); snapshot.Sessions[0].Host = "mutated-snapshot.invalid";
            var previous = File.ReadAllBytes(store.FilePath);
            Check(store.GetSessions().First().Host == existing.Host && Throws<IOException>(() => port.UpdateSessionConfiguration(current =>
            {
                current.Sessions[0].Host = "mutated-failed-transaction.invalid";
                throw new IOException("fixture change failure");
            })) && store.GetSessions().First().Host == existing.Host && previous.SequenceEqual(File.ReadAllBytes(store.FilePath)),
                mode + " snapshots and failed transaction callbacks cannot mutate live or persisted sessions");

            string incomingPath = Path.Combine(root, "rollback-" + mode + ".json");
            File.WriteAllText(incomingPath, JsonSerializer.Serialize(new AppData
                { Groups = ["Rollback/Empty"], Sessions = [new Session { Host = "rollback.invalid" }] }, Store.Json));
            var previousSessions = store.Data.Sessions; var previousGroups = store.Data.Groups;
            Directory.CreateDirectory(store.FilePath + ".tmp");
            Check(Throws<UnauthorizedAccessException>(() => service.Import(incomingPath))
                && ReferenceEquals(previousSessions, store.Data.Sessions) && ReferenceEquals(previousGroups, store.Data.Groups)
                && previous.SequenceEqual(File.ReadAllBytes(store.FilePath)) && store.Data.Credentials.Single().Value == ciphertext
                && store.GetPassword(existing.Id) == "retained-session-secret",
                mode + " failed import restores original collections, persisted bytes and usable existing credentials");
            Directory.Delete(store.FilePath + ".tmp");
            Check(service.Import(incomingPath) == 1 && File.ReadAllBytes(store.FilePath + ".bak").SequenceEqual(previous),
                mode + " successful retry commits once and retains the pre-import atomic backup");

            foreach (var invalid in new[]
            {
                new AppData { SchemaVersion = 99 },
                new AppData { Groups = null! },
                new AppData { Sessions = [existing, existing with { Name = "duplicate" }] },
                new AppData { Sessions = [existing with { Host = "invalid host" }] }
            })
            {
                previous = File.ReadAllBytes(store.FilePath);
                File.WriteAllText(incomingPath, JsonSerializer.Serialize(invalid, Store.Json));
                bool rejected = false;
                try { service.Import(incomingPath); } catch (Exception e) when (e is InvalidDataException or ArgumentException) { rejected = true; }
                Check(rejected && previous.SequenceEqual(File.ReadAllBytes(store.FilePath)) && store.GetSessions().Count == 5,
                    mode + " invalid import is rejected without writes: " + (invalid.SchemaVersion == 99 ? "schema"
                        : invalid.Groups == null ? "structure" : invalid.Sessions.Count > 1 ? "duplicate identity" : "session"));
            }
            File.WriteAllText(incomingPath, JsonSerializer.Serialize(new AppData
                { Groups = ["Concurrent/Empty"], Sessions = [new Session { Host = "parallel.invalid" }] }, Store.Json));
            Parallel.Invoke(() => service.Import(incomingPath), () => service.Import(incomingPath), () => service.AddGroup("Concurrent/Saved"));
            Check(store.GetSessions().Count == 7 && store.GetSessions().Select(s => s.Id).Distinct().Count() == 7
                && store.GetGroups().Contains("Concurrent/Saved") && store.GetGroups().Count(g => g == "Concurrent/Empty") == 1,
                mode + " concurrent imports and category saves merge under the shared lock without lost updates");
            string export = Path.Combine(root, "policy-export-" + mode + ".json"); store.Export(export);
            var exported = JsonSerializer.Deserialize<AppData>(File.ReadAllText(export), Store.Json)!;
            Check(exported.SchemaVersion == 2 && exported.Sessions.Count == 7 && exported.Sessions.All(s => !s.SavePassword)
                && exported.Groups.SequenceEqual(store.GetGroups()) && exported.Vault == null
                && exported.Credentials.Count == 0 && exported.HostKeys.Count == 0
                && store.GetPassword(existing.Id) == "retained-session-secret",
                mode + " legacy Store export delegates the application policy and retains credential-free JSON v2 shape");
            using var restarted = new Store(new WindowsSecretProtector(), TestPaths(directory, portable));
            restarted.UnlockVault("transfer-policy-master");
            Check(restarted.GetSessions().SequenceEqual(store.GetSessions()) && restarted.GetGroups().SequenceEqual(store.GetGroups())
                && restarted.GetPassword(existing.Id) == "retained-session-secret" && restarted.Data.HostKeys.Count == 1,
                mode + " committed imports survive restart with existing credential associations and trust intact");
        }
    }
}
