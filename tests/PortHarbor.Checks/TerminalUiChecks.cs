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
}
