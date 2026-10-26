using System.Text.Json;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void StorageAdapters(string root)
    {
        Check(typeof(Store).Assembly != typeof(MainForm).Assembly && typeof(Store).Assembly.GetReferencedAssemblies().All(a => a.Name != "System.Windows.Forms"),
            "storage adapters live outside the WinForms assembly and do not reference WinForms");
        foreach (bool portable in new[] { false, true })
        {
            string mode = portable ? "INI" : "JSON";
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "adapters-" + mode), false));
            var service = new ConfigurationService(store, store, store);
            var original = new Session { Name = "Existing", Host = "existing.invalid" };
            service.SaveSession(original, "");
            service.AddGroup("Existing/Empty");
            service.CheckHostKey("existing.invalid", 22, "SHA256:existing", _ => true);
            var incoming = new Session { Name = "Imported", Host = "imported.invalid", SavePassword = true };
            string importPath = Path.Combine(root, "incoming-" + mode + ".json");
            File.WriteAllText(importPath, JsonSerializer.Serialize(new AppData
            {
                SchemaVersion = 1, Groups = ["Imported/Empty"], Sessions = [incoming],
                Credentials = new() { [incoming.Id.ToString()] = "fixture-ciphertext-never-used" },
                HostKeys = new() { ["[imported.invalid]:22"] = "SHA256:untrusted" }
            }, Store.Json));
            byte[] previous = File.ReadAllBytes(store.FilePath);
            Directory.CreateDirectory(store.FilePath + ".tmp");
            Check(Throws<UnauthorizedAccessException>(() => service.Import(importPath))
                && service.GetSessions().Single() == original && service.GetGroups().SequenceEqual(["Existing/Empty"])
                && previous.SequenceEqual(File.ReadAllBytes(store.FilePath)), mode + " failed import rolls back groups and sessions and preserves the file");
            Check(Throws<UnauthorizedAccessException>(() => service.CheckHostKey("new.invalid", 22, "SHA256:new", _ => true))
                && !store.Data.HostKeys.ContainsKey("[new.invalid]:22") && previous.SequenceEqual(File.ReadAllBytes(store.FilePath)),
                mode + " failed host trust save does not leave an unsaved trusted fingerprint");
            Directory.Delete(store.FilePath + ".tmp");
            int prompts = 0;
            Check(!service.CheckHostKey("new.invalid", 22, "SHA256:new", _ => { prompts++; return false; }) && prompts == 1,
                mode + " fingerprint whose save failed requires confirmation again");
            bool callbackReadCompleted = false;
            service.CheckHostKey("confirmed.invalid", 22, "SHA256:confirmed", _ =>
            {
                callbackReadCompleted = Task.Run(() => service.GetSessions()).Wait(TimeSpan.FromSeconds(3));
                return true;
            });
            Check(callbackReadCompleted, mode + " host confirmation does not hold the shared storage lock");
            Check(Throws<HostKeyRejectedException>(() => service.CheckHostKey("CONFIRMED.INVALID", 22, "SHA256:changed", _ => throw new Exception("must not prompt"))),
                mode + " changed host fingerprint is rejected through the shared application exception");
            Check(service.Import(importPath) == 1 && service.GetSessions().Count == 2
                && service.GetSessions().Single(s => s.Name == "Imported").Id != incoming.Id
                && !service.GetSessions().Single(s => s.Name == "Imported").SavePassword
                && store.Data.Credentials.Count == 0 && !store.Data.HostKeys.ContainsKey("[imported.invalid]:22"),
                mode + " import assigns a fresh ID and omits incoming credentials and host trust");
            string exportPath = Path.Combine(root, "adapter-export-" + mode + ".json");
            service.Export(exportPath);
            var exported = JsonSerializer.Deserialize<AppData>(File.ReadAllText(exportPath), Store.Json)!;
            Check(exported.Sessions.Count == 2 && exported.Groups.Contains("Imported/Empty") && exported.Credentials.Count == 0
                && exported.HostKeys.Count == 0 && exported.Vault == null, mode + " export preserves sessions and empty groups without credential or trust state");
            using var restarted = new Store(new WindowsSecretProtector(), TestPaths(store.DirectoryPath, false));
            Check(restarted.GetSessions().SequenceEqual(service.GetSessions()) && restarted.GetGroups().SequenceEqual(service.GetGroups())
                && restarted.CheckHostKey("confirmed.invalid", 22, "SHA256:confirmed", _ => throw new Exception("must not prompt")),
                mode + " adapter restart preserves imported sessions and separately confirmed host trust");
        }
    }
}
