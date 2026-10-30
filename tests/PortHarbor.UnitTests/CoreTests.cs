using NUnit.Framework;
using PortHarbor.Checks;

namespace PortHarbor.UnitTests;

[TestFixture]
public sealed class CoreTests
{
    static void Check(bool condition, string name) => Assert.That(condition, Is.True, name);

    [Test] public void ApplicationServices() => ApplicationServiceChecks.Run(Check);
    [Test] public void SessionValidation() => SessionValidationChecks.Run(Check);
    [Test] public void SessionDraftDefaults() => SessionDraftChecks.Run(Check);
    [Test] public void SessionLibrary() => SessionLibraryChecks.Run(Check);
    [Test] public void SessionActivity() => SessionActivityChecks.Run(Check);
    [Test] public void ConfigurationTransfer() => ConfigurationTransferChecks.Run(Check);
    [Test] public void WorkspacePlacement() => WorkspacePlacementChecks.Run(Check);
    [Test] public void DiagnosticMessages() => DiagnosticMessageChecks.Run(Check);
    [Test] public void RemoteDesktopConfiguration() => RemoteDesktopChecks.Run(Check);
    [Test] public void PlatformCapabilities() => PlatformCapabilityChecks.Run(Check);
    [Test] public Task TerminalLifecycle() => TerminalSessionChecks.RunAsync(Check);
    [Test] public Task TransferQueue() => TransferQueueChecks.RunAsync(Check);
    [Test] public void TerminalStateMachine() => TerminalModelChecks.Run(Check);
    [Test] public void TelnetCodec() => TelnetModelChecks.Run(Check);

    [Test]
    public async Task FileTransferPolicy()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortHarbor-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { await FileTransferPolicyChecks.RunAsync(root, Check); }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void DiagnosticBundle()
    {
        string root = Path.Combine(Path.GetTempPath(), "PortHarbor-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { DiagnosticExportChecks.Run(root, Check); }
        finally { Directory.Delete(root, true); }
    }
}
