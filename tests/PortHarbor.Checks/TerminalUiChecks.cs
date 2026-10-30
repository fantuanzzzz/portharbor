using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void TerminalCloseUi(string root)
    {
        foreach (bool shutdown in new[] { false, true })
        {
            var backend = new ScriptedTerminalTransport { BlockDispose = true };
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "terminal-close-ui-" + shutdown), false));
            using var form = new MainForm(store, new ConfigurationService(store, store, store),
                (session, _, _) => new TerminalConnection(session, backend));
            try
            {
                form.Show(); Application.DoEvents();
                form.Connect(new Session { Name = "test terminal", Host = "fixture.invalid" }, "");
                var tabs = FindControl<TabControl>(form); var page = tabs.SelectedTab!;
                PumpUntil(() => backend.ReadStarted.Task.IsCompleted);
                Task? close = null;
                if (shutdown)
                {
                    // Skip the interactive confirmation in this injected-session lifecycle test.
                    page.Tag = null;
                    form.Close();
                }
                else close = form.CloseTab(page, () => true);
                PumpUntil(() => backend.DisposeStarted.Task.IsCompleted);
                Check(!page.IsDisposed && !form.IsDisposed && (close == null || !close.IsCompleted),
                    (shutdown ? "application shutdown" : "terminal tab close") + " keeps controls alive until terminal cleanup completes");
                backend.DisposeRelease.TrySetResult();
                PumpUntil(() => shutdown ? form.IsDisposed : close!.IsCompleted);
                close?.GetAwaiter().GetResult();
                Check(backend.DisposeCalls == 1 && (shutdown ? form.IsDisposed : page.IsDisposed),
                    (shutdown ? "application shutdown" : "terminal tab close") + " awaits terminal cancellation and disposes it once");
                if (!shutdown) { form.Close(); PumpUntil(() => form.IsDisposed); }
            }
            finally { backend.DisposeRelease.TrySetResult(); }
        }
    }

    static void TerminalSearchUi()
    {
        using var terminal = new TerminalView(); terminal.CreateControl();
        terminal.Screen.Feed("first line\r\nMATCH target\r\nlast line");
        string snapshot = terminal.Screen.Text;
        int found = terminal.FindText("match", 0);
        Check(found >= 0 && terminal.Screen.Text == snapshot, "terminal find searches the existing screen/history case-insensitively without sending input or changing buffer contents");
        int absent = terminal.FindText("not present", 0);
        Check(absent == -1 && terminal.Screen.Text == snapshot, "terminal find reports no match and leaves the existing buffer unchanged");
        Check(terminal.FindText("", 0) == -1 && terminal.Screen.Text == snapshot, "empty terminal search is a no-op on the current buffer");
    }

    static void TerminalReconnectUi(string root)
    {
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "terminal-reconnect-ui"), true));
        var instances = new List<ScriptedTerminalTransport>();
        var session = new Session { Name = "reconnect fixture", Host = "fixture.invalid" };
        using var form = new MainForm(store, new ConfigurationService(store, store, store),
            (saved, _, _) =>
            {
                var transport = new ScriptedTerminalTransport(); instances.Add(transport);
                return new TerminalConnection(saved, transport);
            });
        form.Show(); Application.DoEvents(); form.Connect(session, "");
        PumpUntil(() => instances.Count == 1 && instances[0].ReadStarted.Task.IsCompleted);
        var page = FindControl<TabControl>(form).SelectedTab!;
        var terminal = FindControl<TerminalView>(page); terminal.Screen.Feed("old-buffer-marker");
        var retry = Descendants(page).OfType<Button>().Single(b => b.Name == "ReconnectTerminal");
        retry.PerformClick(); PumpUntil(() => instances.Count == 2);
        PumpUntil(() => instances[0].DisposeStarted.Task.IsCompleted);
        Check(instances.Count == 2 && instances[0].DisposeCalls == 1 && ReferenceEquals(FindControl<TabControl>(form).SelectedTab, page)
            && instances[1].Written.IsEmpty,
            "reconnect awaits disposal then creates a fresh session in the same tab without replaying input");
        foreach (TabPage tab in FindControl<TabControl>(form).TabPages) tab.Tag = null;
        form.Close(); PumpUntil(() => form.IsDisposed);
    }
}
