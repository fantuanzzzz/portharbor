using System.Text.Json;
using PortHarbor;

var results = new List<string>();
string? failure = null;
string transferRoot = Path.Combine(Path.GetTempPath(), "PortHarbor-application-check-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(transferRoot);
    PortHarbor.Checks.ApplicationServiceChecks.Run(Check);
    PortHarbor.Checks.SessionDraftChecks.Run(Check);
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
