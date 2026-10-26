using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void WindowsCapabilityDetection()
    {
        int probes = 0;
        var unsupported = new WindowsPlatformCapabilities(() => false, (_, _) => { probes++; return true; }, () => { probes++; return true; });
        Check(Enum.GetValues<PlatformCapability>().All(c => unsupported.Get(c) == new PlatformCapabilityStatus(false, PlatformUnavailableReason.UnsupportedOperatingSystem))
            && probes == 0, "unsupported OS capability detection never loads native libraries or inspects desktop clients");
        var exports = new Dictionary<string, string[]>();
        bool clientExists = false;
        var windows = new WindowsPlatformCapabilities(() => true, (library, names) => { exports[library] = names; return library == "crypt32.dll"; }, () => clientExists);
        Check(windows.Get(PlatformCapability.SecretProtection).IsAvailable && exports["crypt32.dll"].SequenceEqual(["CryptProtectData", "CryptUnprotectData"]),
            "Windows credential capability requires both DPAPI operations without encrypting a probe secret");
        Check(windows.Get(PlatformCapability.LocalShell).Reason == PlatformUnavailableReason.MissingSystemComponent
            && exports["kernel32.dll"].SequenceEqual(["CreatePseudoConsole", "ResizePseudoConsole", "ClosePseudoConsole"]),
            "Windows shell capability requires create resize and close pseudoconsole exports");
        Check(!windows.Get(PlatformCapability.RemoteDesktop).IsAvailable, "missing mstsc is reported as unavailable desktop capability");
        clientExists = true;
        Check(windows.Get(PlatformCapability.RemoteDesktop).IsAvailable, "desktop client detection refreshes after system support changes");
        Check(windows.Get(PlatformCapability.SerialPort).IsAvailable, "Windows serial support does not require an enumerated or open device");
        var denied = new WindowsPlatformCapabilities(() => true, (_, _) => throw new UnauthorizedAccessException("private path"),
            () => throw new IOException("private executable path"));
        Check(denied.Get(PlatformCapability.LocalShell).Reason == PlatformUnavailableReason.InspectionFailed
            && denied.Get(PlatformCapability.RemoteDesktop).Reason == PlatformUnavailableReason.InspectionFailed,
            "capability probe failures return a stable reason without exposing system exception messages");
        Check(Throws<ArgumentOutOfRangeException>(() => windows.Get((PlatformCapability)999)), "undefined capabilities are rejected without probing");
    }

    static void WindowsCapabilityRuntime()
    {
        var capabilities = new WindowsPlatformCapabilities();
        Check(capabilities.Get(PlatformCapability.LocalShell).IsAvailable && capabilities.Get(PlatformCapability.SecretProtection).IsAvailable
            && capabilities.Get(PlatformCapability.SerialPort).IsAvailable,
            "current Windows runtime exposes the DPAPI ConPTY and serial prerequisites used by platform adapters");
        Check(capabilities.Get(PlatformCapability.RemoteDesktop).IsAvailable == File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe")),
            "current Windows desktop capability matches the exact system mstsc path used by the launcher");
    }

    static void CapabilityUi(string root)
    {
        var capabilities = new FixturePlatformCapabilities();
        var missing = new PlatformCapabilityStatus(false, PlatformUnavailableReason.MissingSystemComponent);
        foreach (var capability in Enum.GetValues<PlatformCapability>()) capabilities.States[capability] = missing;
        foreach (var protocol in new[] { Protocol.Shell, Protocol.Serial, Protocol.RDP })
        {
            var session = new Session { Protocol = protocol, Host = "fixture.invalid", SerialPort = "COM42" };
            using var editor = new SessionEditor(session, serialPorts: new CapabilitySerialProvider(), capabilities: capabilities);
            editor.Show(); Application.DoEvents();
            Check(!Descendants(editor).OfType<Button>().Single(b => b.Text == "保存并连接").Enabled
                && Descendants(editor).Single(c => c.Name == "PlatformAvailability").Text.Contains("不可用")
                && Descendants(editor).OfType<ComboBox>().Any(b => b.Text == protocol.ToString()),
                protocol + " editor preserves protocol selection and explains why connecting is disabled");
            Descendants(editor).OfType<Button>().Single(b => b.Text == "仅保存").PerformClick();
            Check(editor.DialogResult == DialogResult.OK && !editor.ConnectNow && editor.Persist
                && editor.Result.Id == session.Id && editor.Result.Protocol == protocol,
                protocol + " unavailable capability still permits saving the original session identity and protocol");
        }
        capabilities.States[PlatformCapability.SerialPort] = PlatformCapabilityStatus.Available;
        foreach (bool failEnumeration in new[] { false, true })
        {
            using var editor = new SessionEditor(new Session { Protocol = Protocol.Serial, SerialPort = "COM42" },
                serialPorts: new CapabilitySerialProvider { Fail = failEnumeration }, capabilities: capabilities);
            editor.Show(); Application.DoEvents();
            Check(Descendants(editor).OfType<Button>().Single(b => b.Text == "保存并连接").Enabled
                && Descendants(editor).Single(c => c.Name == "SerialNotice").Text.Contains("手动输入")
                && Descendants(editor).OfType<ComboBox>().Any(b => b.Text == "COM42"),
                (failEnumeration ? "failed" : "empty") + " serial enumeration permits manual port selection without disabling serial support");
            editor.Close();
        }
        using (var editor = new SessionEditor(new Session { Protocol = Protocol.RDP, Host = "fixture.invalid" },
            serialPorts: new CapabilitySerialProvider(), capabilities: capabilities))
        {
            editor.Show(); Application.DoEvents();
            var choices = Descendants(editor).OfType<ComboBox>().Single(b => b.Items.Contains("RDP") && b.Items.Contains("SSH"));
            choices.SelectedItem = "SSH";
            Check(Descendants(editor).OfType<Button>().Single(b => b.Text == "保存并连接").Enabled
                && !Descendants(editor).Single(c => c.Name == "PlatformAvailability").Visible,
                "changing from unavailable RDP to SSH clears the capability restriction");
            // A capability that disappears between presentation and clicking is checked again by Finish.
            capabilities.States[PlatformCapability.RemoteDesktop] = PlatformCapabilityStatus.Available; choices.SelectedItem = "RDP";
            capabilities.States[PlatformCapability.RemoteDesktop] = missing;
            Descendants(editor).OfType<Button>().Single(b => b.Text == "保存并连接").PerformClick();
            Check(editor.Visible && editor.DialogResult != DialogResult.OK,
                "editor rechecks capability at click time before accepting a connection");
            editor.Close();
        }

        capabilities.States[PlatformCapability.SerialPort] = missing;
        int terminalStarts = 0;
        var desktop = new FixtureDesktopLauncher { Fail = false };
        var transport = new ScriptedTerminalTransport();
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "capability-ui"), false));
        using var form = new MainForm(store, new ConfigurationService(store, store, store),
            (session, _, _) => { terminalStarts++; return new TerminalConnection(session, transport); }, desktop, capabilities);
        form.Show(); Application.DoEvents();
        var tabs = FindControl<TabControl>(form);
        var savedDesktop = new Session { Protocol = Protocol.RDP, Host = "fixture.invalid", SavePassword = true };
        store.SetMasterPassword("capability-test-master"); store.PutSession(savedDesktop, "capability-test-secret");
        foreach (var protocol in new[] { Protocol.Shell, Protocol.Serial, Protocol.RDP })
        {
            form.Connect(protocol == Protocol.RDP ? savedDesktop : new Session { Protocol = protocol, Host = "fixture.invalid", SerialPort = "COM42" }, "");
            Check(terminalStarts == 0 && desktop.Calls == 0
                && Descendants(tabs.SelectedTab!).Single(c => c.Name == "PlatformUnavailable").Text.Contains("不可用")
                && FindControl<ConnectionStatusLabel>(tabs.SelectedTab!).Text.Contains("平台资源不可用"),
                protocol + " connection entry displays a capability failure before creating a transport or desktop client");
        }
        var rdpPage = tabs.SelectedTab!; int pageCount = tabs.TabPages.Count;
        Descendants(rdpPage).OfType<Button>().Single(b => b.Text == "重新检查").PerformClick();
        Check(desktop.Calls == 0, "rechecking an unavailable desktop capability does not start the client");
        capabilities.States[PlatformCapability.RemoteDesktop] = PlatformCapabilityStatus.Available;
        Descendants(rdpPage).OfType<Button>().Single(b => b.Text == "重新检查").PerformClick();
        Check(desktop.Calls == 1 && tabs.SelectedTab == rdpPage && tabs.TabPages.Count == pageCount
            && FindControl<ConnectionStatusLabel>(rdpPage).Text == "已启动 Windows 远程桌面客户端",
            "recovered desktop capability starts the injected client in the existing tab");
        Check(Descendants(rdpPage).OfType<Button>().Any(b => b.Text.StartsWith("复制会话密码")),
            "recovered saved RDP session reads its credential from an unlocked vault after capability preflight");
        capabilities.States[PlatformCapability.RemoteDesktop] = missing;
        Descendants(rdpPage).OfType<Button>().Single(b => b.Text == "打开远程桌面").PerformClick();
        Check(desktop.Calls == 1 && Descendants(rdpPage).Any(c => c.Name == "PlatformUnavailable"),
            "RDP retry rechecks capability before launching another external client");
        form.Connect(new Session { Protocol = Protocol.SSH, Host = "fixture.invalid" }, "");
        var sshPage = tabs.SelectedTab!;
        PumpUntil(() => transport.ReadStarted.Task.IsCompleted);
        Check(terminalStarts == 1, "SSH connection entry remains usable when all local capabilities are unavailable");
        var close = form.CloseTab(sshPage, () => true); PumpUntil(() => close.IsCompleted); close.GetAwaiter().GetResult();
        foreach (TabPage page in tabs.TabPages) page.Tag = null;
        form.Close(); Application.DoEvents();
    }

    sealed class CapabilitySerialProvider : ISerialPortProvider
    {
        internal bool Fail;
        public IReadOnlyList<string> GetPortNames() => Fail ? throw new UnauthorizedAccessException("private device path") : [];
        public IReadOnlyList<string> ParityOptions => ["None"];
        public IReadOnlyList<string> StopBitOptions => ["One"];
        public IReadOnlyList<string> HandshakeOptions => ["None"];
    }
}
