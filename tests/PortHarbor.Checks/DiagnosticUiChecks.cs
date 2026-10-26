using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void DiagnosticUi(string root)
    {
        using var host = new Form { Width = 800, Height = 200 };
        var label = new ConnectionStatusLabel { Dock = DockStyle.Fill };
        host.Controls.Add(label); host.Show(); Application.DoEvents();
        var failure = new ConnectionDiagnostic(Guid.NewGuid(), Protocol.SSH, ConnectionState.Failed,
            ConnectionStage.Authenticating, ConnectionErrorCode.AuthenticationFailed, false, "private-secret\x1b[31m");
        var report = Task.Run(() =>
        {
            label.Report(failure);
            label.Report(failure with { State = ConnectionState.Closed, Stage = ConnectionStage.Closing, ErrorCode = ConnectionErrorCode.Cancelled });
            label.Report(failure with { State = ConnectionState.Closing, Stage = ConnectionStage.Closing, ErrorCode = ConnectionErrorCode.None });
            label.Report(failure with { State = ConnectionState.Closed, Stage = ConnectionStage.Closing, ErrorCode = ConnectionErrorCode.None });
        });
        PumpUntil(() => report.IsCompleted && label.Text.Contains("身份验证失败")); report.GetAwaiter().GetResult();
        Check(label.Text.Contains(failure.OperationId.ToString("N")[..8]) && !label.Text.Contains("private"),
            "worker-thread diagnostics retain a safe failure after connection cleanup");
        label.Report(failure with { State = ConnectionState.Connecting, Stage = ConnectionStage.Connecting, ErrorCode = ConnectionErrorCode.None });
        label.Report(failure with { State = ConnectionState.Connected, ErrorCode = ConnectionErrorCode.None });
        Check(label.Text == "已连接", "new connection clears the previous diagnostic failure");
        label.Dispose();
        Task.Run(() => label.Report(failure)).GetAwaiter().GetResult();
        Check(label.IsDisposed, "late diagnostic after view disposal is ignored safely");
        host.Close();

        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "diagnostic-ui"), false));
        using var form = new MainForm(store); form.Show(); Application.DoEvents();
        form.Connect(new Session { Protocol = Protocol.Shell, Executable = "private-secret-nonexistent-shell.exe", Arguments = "", Name = "failed shell" }, "");
        var tabs = FindControl<TabControl>(form);
        var page = tabs.SelectedTab!;
        var terminal = FindControl<TerminalView>(page);
        var connectionStatus = FindControl<ConnectionStatusLabel>(page);
        PumpUntil(() => connectionStatus.Text.Contains("平台资源不可用") && terminal.Screen.Text.Contains("操作编号"));
        Check(!terminal.Screen.Text.Contains("private-secret") && connectionStatus.Text.Contains("操作编号"),
            "main window presents shell failure through safe diagnostics in terminal and status bar");
        var close = form.CloseTab(page, () => true);
        PumpUntil(() => close.IsCompleted); close.GetAwaiter().GetResult();
        Check(page.IsDisposed, "terminal diagnostic page can close after startup failure");
        form.Close(); Application.DoEvents();

        var desktop = new FixtureDesktopLauncher();
        using var rdpForm = new MainForm(store, new ConfigurationService(store, store, store), remoteDesktopLauncher: desktop,
            capabilities: new FixturePlatformCapabilities());
        rdpForm.Show(); Application.DoEvents();
        rdpForm.Connect(new Session { Protocol = Protocol.RDP, Host = "private-host", Name = "RDP fixture" }, "");
        var rdpPage = FindControl<TabControl>(rdpForm).SelectedTab!;
        var rdpStatus = FindControl<ConnectionStatusLabel>(rdpPage);
        Check(desktop.Calls == 1 && rdpStatus.Text.Contains("平台资源不可用") && !rdpStatus.Text.Contains("private"),
            "RDP page uses the injected platform port and presents safe startup failure diagnostics");
        desktop.Fail = false;
        Descendants(rdpPage).OfType<Button>().Single(b => b.Text == "打开远程桌面").PerformClick();
        Check(desktop.Calls == 2 && rdpStatus.Text == "已启动 Windows 远程桌面客户端",
            "RDP page retry invokes the platform port again and reports external client startup");
        rdpPage.Tag = null; // Skip the interactive exit confirmation in this injected-client check.
        rdpForm.Close(); Application.DoEvents();
    }

    static void PumpUntil(Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            Application.DoEvents(); Thread.Sleep(10);
        }
        if (!done()) throw new TimeoutException("Diagnostic UI check did not complete.");
    }

    sealed class FixtureDesktopLauncher : IRemoteDesktopLauncher
    {
        internal int Calls;
        internal bool Fail = true;
        public void Launch(string configuration, string directory, Action<ConnectionStage> reportStage)
        {
            Calls++; reportStage(ConnectionStage.Writing); reportStage(ConnectionStage.LaunchingClient);
            if (Fail) throw new System.ComponentModel.Win32Exception(2, "private client path");
        }
    }
}
