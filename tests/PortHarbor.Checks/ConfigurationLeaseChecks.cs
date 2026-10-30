using System.Diagnostics;
using System.Text.Json;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static async Task<Process> StartOwnershipProbe(string option, string directory)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "PortHarbor.Checks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(option); info.ArgumentList.Add(directory);
        var child = Process.Start(info)!;
        try
        {
            string? ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (ready != "READY") throw new IOException("Ownership probe did not start: " + await child.StandardError.ReadToEndAsync());
            return child;
        }
        catch { if (!child.HasExited) child.Kill(); child.Dispose(); throw; }
    }

    static async Task ConfigurationLeaseChecks(string root)
    {
        string directory = Path.Combine(root, "lease");
        using (ConfigurationDirectoryLease.Acquire(directory))
        {
            Check(Throws<IOException>(() => ConfigurationDirectoryLease.Acquire(Path.Combine(directory, "..", "lease"))),
                "configuration lease rejects another writer using an equivalent directory path");
            Check(Throws<IOException>(() => ConfigurationDirectoryLease.Acquire(directory.ToUpperInvariant())),
                "configuration lease rejects Windows case aliases");
            using var other = ConfigurationDirectoryLease.Acquire(Path.Combine(root, "other-lease"));
            Check(other != null, "different configuration directories can be opened independently");
        }
        using (ConfigurationDirectoryLease.Acquire(directory))
            Check(true, "normal disposal releases the lease without deleting the reusable lock file");
        foreach (bool crash in new[] { false, true })
        {
            using var child = await StartOwnershipProbe("--lease-probe", directory);
            try
            {
                Check(Throws<IOException>(() => ConfigurationDirectoryLease.Acquire(directory)),
                    "an independent process owns the configuration directory exclusively");
                if (crash) child.Kill(); else await child.StandardInput.WriteLineAsync("exit");
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                using var recovered = await AcquireReleasedLease(directory);
                Check(true, crash ? "process crash releases the configuration lease automatically" : "normal process exit releases the configuration lease");
            }
            finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
        }
    }

    static async Task<ConfigurationDirectoryLease> AcquireReleasedLease(string directory)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { return ConfigurationDirectoryLease.Acquire(directory); }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                // A terminated process can be signalled before Windows finishes releasing its file objects.
                await Task.Delay(25);
            }
        }
    }

    static async Task RdpOrphanChecks(string root)
    {
        string directory = Path.Combine(root, "rdp-orphans"); Directory.CreateDirectory(directory);
        string legacy = Path.Combine(directory, "rdp-legacy.rdp"); File.WriteAllText(legacy, "user file");
        using (var active = RdpTemporaryWorkspace.Create(directory))
        {
            File.WriteAllText(active.ConfigurationPath, "active");
            RdpTemporaryWorkspace.Sweep(directory, (_, _) => false);
            Check(File.Exists(active.ConfigurationPath), "RDP sweep preserves a workspace whose owner lease is still active");
        }

        string Fixture(string state, int id = 123, long started = 456)
        {
            string path = Path.Combine(directory, "rdp-temporary", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, ".owner.lock"), "");
            File.WriteAllText(Path.Combine(path, "connection.rdp"), "fixture");
            File.WriteAllText(Path.Combine(path, "owner.json"), JsonSerializer.Serialize(new RdpTemporaryWorkspace.Owner("PortHarbor.RdpTemporary.v1", state, id, started)));
            return path;
        }

        string dead = Fixture("started"), live = Fixture("started"), unknown = Fixture("started"), launching = Fixture("launching");
        string foreign = Fixture("created"), malformed = Fixture("created");
        File.WriteAllText(Path.Combine(foreign, "notes.txt"), "user file");
        File.WriteAllText(Path.Combine(malformed, "owner.json"), "invalid json");
        RdpTemporaryWorkspace.Sweep(directory, (_, _) => false);
        Check(!Directory.Exists(dead), "RDP sweep removes an abandoned workspace after the recorded client has exited");
        // Recreate fixtures removed by the preceding dead-client sweep.
        live = Fixture("started"); unknown = Fixture("started");
        RdpTemporaryWorkspace.Sweep(directory, (_, _) => true);
        Check(Directory.Exists(live), "RDP sweep preserves an external client that outlived PortHarbor");
        RdpTemporaryWorkspace.Sweep(directory, (_, _) => null);
        Check(Directory.Exists(unknown), "RDP sweep preserves files when external process identity cannot be inspected");
        Check(Directory.Exists(launching) && Directory.Exists(foreign) && Directory.Exists(malformed) && File.Exists(legacy),
            "RDP sweep preserves uncertain launches, unknown files, invalid ownership and legacy user files");
        bool identityChecked = false;
        RdpTemporaryWorkspace.Sweep(directory, (id, ticks) => { identityChecked = id == 123 && ticks == 456; return false; });
        Check(identityChecked && !Directory.Exists(live), "RDP cleanup compares client PID and start time to handle process-ID reuse");

        using (var current = Process.GetCurrentProcess())
        {
            string running = Fixture("started", current.Id, current.StartTime.ToUniversalTime().Ticks);
            string reused = Fixture("started", current.Id, current.StartTime.ToUniversalTime().Ticks - 1);
            RdpTemporaryWorkspace.Sweep(directory);
            Check(Directory.Exists(running) && !Directory.Exists(reused),
                "RDP sweep inspects actual OS process identity and distinguishes a reused PID from its recorded client");
        }

        using var crashed = await StartOwnershipProbe("--rdp-orphan-probe", directory);
        try
        {
            int before = Directory.GetDirectories(Path.Combine(directory, "rdp-temporary")).Length;
            crashed.Kill(); await crashed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var elapsed = Stopwatch.StartNew();
            do
            {
                RdpTemporaryWorkspace.Sweep(directory);
                if (Directory.GetDirectories(Path.Combine(directory, "rdp-temporary")).Length < before) break;
                await Task.Delay(25);
            } while (elapsed.Elapsed < TimeSpan.FromSeconds(5));
            Check(Directory.GetDirectories(Path.Combine(directory, "rdp-temporary")).Length == before - 1,
                "RDP cleanup recovers a managed prelaunch workspace after an actual owner process crash");
        }
        finally { if (!crashed.HasExited) { crashed.Kill(); await crashed.WaitForExitAsync(); } }
    }

    static async Task ApplicationLeaseStartupChecks(string root)
    {
        string directory = Path.Combine(root, "app-startup-lease");
        async Task<int> Start(string dataDirectory)
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "PortHarbor.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
            foreach (string value in new[] { "--smoke", "--portable", "--data-dir", dataDirectory }) info.ArgumentList.Add(value);
            using var child = Process.Start(info)!;
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); return child.ExitCode; }
            finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } }
        }
        using (ConfigurationDirectoryLease.Acquire(directory))
        {
            Check(await Start(directory) == 1 && !File.Exists(Path.Combine(directory, "PortHarbor.ini")),
                "application startup refuses an occupied configuration directory before loading or writing its Store");
            Check(await Start(Path.Combine(root, "app-independent")) == 0,
                "application startup supports an independent data directory while another directory is leased");
        }
        Check(await Start(directory) == 0 && File.Exists(Path.Combine(directory, "PortHarbor.ini")),
            "application startup succeeds and creates portable data after the previous lease is released");
    }
}
