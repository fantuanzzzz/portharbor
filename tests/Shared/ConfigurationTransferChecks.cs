using PortHarbor;

namespace PortHarbor.Checks;

internal static class ConfigurationTransferChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var typed = new Session { Protocol = Protocol.Serial, SerialPort = "COM7", BaudRate = 9600 }.ProtocolConfiguration;
        check(typed is SerialProtocolConfiguration serial && serial.PortName == "COM7" && serial.BaudRate == 9600,
            "protocol configuration exposes a typed serial view without changing the persisted session shape");
        var network = new Session { Protocol = Protocol.SFTP, Host = "host.invalid", Port = 2222, Username = "u", PrivateKey = "key", UseTls = false }.ProtocolConfiguration;
        var shell = new Session { Protocol = Protocol.Shell, Executable = "pwsh.exe", Arguments = "-NoLogo", WorkingDirectory = "C:\\" }.ProtocolConfiguration;
        var rdp = new Session { Protocol = Protocol.RDP, Host = "desktop.invalid", Port = 3390, Username = "u", RdpFullScreen = true, Clipboard = false }.ProtocolConfiguration;
        check(network is SftpProtocolConfiguration n && n.Host == "host.invalid" && n.Port == 2222 && n.PrivateKey == "key"
            && shell is ShellProtocolConfiguration sh && sh.Executable == "pwsh.exe"
            && rdp is RdpProtocolConfiguration r && r.FullScreen && !r.Clipboard,
            "protocol configuration views preserve network, shell and RDP options without changing legacy DTOs");
        var existing = new Session { Host = "existing.invalid", SavePassword = true };
        var incoming = existing with { Name = "导入 🔑", Group = "Work/New", PrivateKey = "private key path" };
        var storage = new FixtureTransfer
        {
            Current = new(["Work/Empty"], [existing]),
            Incoming = new(["work/empty", "Imported/Empty", "imported/empty"], [incoming])
        };
        var service = new ConfigurationTransferService(storage);
        check(typeof(ConfigurationTransferService).Assembly == typeof(SessionService).Assembly
            && typeof(SessionConfiguration).GetProperties().Select(p => p.Name).Order().SequenceEqual(new[] { "Groups", "Sessions" }),
            "configuration transfer policy lives in Application and its DTO exposes only groups and sessions");
        int imported = service.Import("选择 空格.json");
        var first = storage.Current.Sessions.Last();
        check(imported == 1 && storage.ReadPath == "选择 空格.json" && storage.Updates == 1
            && storage.Current.Groups.SequenceEqual(["Work/Empty", "Imported/Empty"]),
            "import merges groups once with stable order and case-insensitive duplicate removal");
        check(storage.Current.Sessions.First() == existing && storage.Current.Sessions.First().SavePassword
            && first.Id != existing.Id && !first.SavePassword && first with { Id = incoming.Id, SavePassword = true } == incoming,
            "import preserves existing sessions and saved-password policy while allocating a fresh identity for incoming settings");
        check(incoming.Id == existing.Id && incoming.SavePassword && storage.Incoming.Sessions.Single() == incoming,
            "import leaves caller-owned incoming sessions and identities untouched");
        service.Import("same-file.json");
        check(storage.Current.Sessions.Count == 3 && storage.Current.Sessions.Select(s => s.Id).Distinct().Count() == 3
            && storage.Current.Groups.Count == 2,
            "repeated import creates independent session identities without duplicating empty categories");
        service.Export("导出 空格.json");
        check(storage.WritePath == "导出 空格.json" && storage.Written!.Sessions.All(s => !s.SavePassword)
            && storage.Written.Sessions.Select(s => s.Id).SequenceEqual(storage.Current.Sessions.Select(s => s.Id))
            && storage.Written.Groups.SequenceEqual(storage.Current.Groups) && existing.SavePassword,
            "export keeps groups, order and identities but disables password saving on independent copies");
        storage.Written!.Sessions[0].Host = "mutated-export.invalid";
        check(storage.Current.Sessions.First().Host == "existing.invalid",
            "mutating an export snapshot cannot alter live session state");

        var before = storage.Current;
        storage.ReadError = new IOException("fixture read failure"); int updates = storage.Updates;
        check(Throws<IOException>(() => service.Import("broken.json")) && storage.Updates == updates && ReferenceEquals(storage.Current, before),
            "file read failure propagates before any configuration update");
        storage.ReadError = null; storage.CommitError = new IOException("fixture atomic-save failure");
        check(Throws<IOException>(() => service.Import("valid.json")) && ReferenceEquals(storage.Current, before)
            && storage.Current.Sessions.Count == 3 && storage.Current.Sessions.First().SavePassword,
            "commit failure propagates without publishing a partially merged configuration");
        storage.CommitError = null; storage.WriteError = new IOException("fixture export failure");
        check(Throws<IOException>(() => service.Export("blocked.json")) && ReferenceEquals(storage.Current, before),
            "export write failure leaves active configuration unchanged and does not report success");
        storage.WriteError = null;

        foreach (var invalid in new[]
        {
            new SessionConfiguration(["invalid\ncategory"], [incoming]),
            new SessionConfiguration([], [incoming with { Host = "invalid host" }]),
            new SessionConfiguration([], [incoming, incoming with { Name = "duplicate ID" }]),
            new SessionConfiguration([], [null!])
        })
        {
            storage.Incoming = invalid; updates = storage.Updates;
            bool rejected = false;
            try { service.Import("invalid.json"); } catch (Exception e) when (e is InvalidDataException or ArgumentException) { rejected = true; }
            check(rejected && storage.Updates == updates && ReferenceEquals(storage.Current, before),
                "invalid incoming configuration is rejected before writes: " + (invalid.Groups.Count > 0 ? "group"
                    : invalid.Sessions.Count > 1 ? "duplicate identity" : invalid.Sessions[0] == null ? "null session" : "session validation"));
        }
        storage.Incoming = new(["New/Empty"], []);
        check(service.Import("groups-only.json") == 0 && storage.Current.Sessions.Count == 3
            && storage.Current.Groups.Contains("New/Empty"),
            "groups-only import returns zero sessions while preserving empty categories");
        var concurrent = new Session { Host = "saved-before-commit.invalid" };
        storage.BeforeChange = () => storage.Current = new(storage.Current.Groups, storage.Current.Sessions.Append(concurrent).ToArray());
        storage.Incoming = new([], [incoming]); service.Import("concurrent.json");
        check(storage.Current.Sessions.Any(s => s.Id == concurrent.Id) && storage.Current.Sessions.Count == 5,
            "import merges against the snapshot obtained under the transaction rather than a stale pre-read snapshot");
        storage.BeforeChange = null;
        foreach (var protocol in Enum.GetValues<Protocol>())
        {
            storage.Incoming = new([], [incoming with { Protocol = protocol, Port = Session.DefaultPort(protocol) }]);
            service.Import("protocol.json");
        }
        check(storage.Current.Sessions.TakeLast(7).Select(s => s.Protocol).SequenceEqual(Enum.GetValues<Protocol>())
            && storage.Current.Sessions.TakeLast(7).All(s => !s.SavePassword && s.PrivateKey == incoming.PrivateKey),
            "all seven protocols preserve their settings across the shared import policy");
    }

    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    sealed class FixtureTransfer : IConfigurationTransfer
    {
        internal SessionConfiguration Current = new([], []), Incoming = new([], []);
        internal SessionConfiguration? Written;
        internal string? ReadPath, WritePath;
        internal int Updates;
        internal IOException? ReadError, CommitError, WriteError;
        internal Action? BeforeChange;
        public SessionConfiguration ReadSessionFile(string path)
        { ReadPath = path; if (ReadError != null) throw ReadError; return Incoming; }
        public void WriteSessionFile(string path, SessionConfiguration configuration)
        { WritePath = path; if (WriteError != null) throw WriteError; Written = configuration; }
        public SessionConfiguration GetSessionConfiguration() => Current;
        public void UpdateSessionConfiguration(Func<SessionConfiguration, SessionConfiguration> change)
        {
            Updates++; BeforeChange?.Invoke(); var updated = change(Current);
            if (CommitError != null) throw CommitError; Current = updated;
        }
    }
}
