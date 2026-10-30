using System.Drawing;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void TerminalPreferencesStorage(string root)
    {
        foreach (bool portable in new[] { false, true })
        {
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "preferences-" + portable), portable));
            store.PutSession(new Session { Host = "kept.invalid" }, "");
            byte[] sessions = File.ReadAllBytes(store.FilePath);
            string path = Path.Combine(store.DirectoryPath, "terminal-preferences.json");
            var service = new TerminalPreferencesService(new TerminalPreferencesRepository(store.Paths)); service.Load();
            Check(service.Current == new TerminalPreferences() && !service.LoadFailed && !File.Exists(path),
                $"{portable} missing terminal preferences use defaults without writing a file");
            var expected = new TerminalPreferences { FontFamily = "Consolas", FontSize = 14.5f, FontStyle = 1, ForegroundRgb = 0xFAEED1, BackgroundRgb = 0x192A34, CopyOnSelection = true, PasteOnRightClick = true };
            service.Save(expected);
            var restarted = new TerminalPreferencesService(new TerminalPreferencesRepository(store.Paths)); restarted.Load();
            Check(restarted.Current == expected && !restarted.LoadFailed && sessions.SequenceEqual(File.ReadAllBytes(store.FilePath)),
                $"{portable} terminal preferences survive restart without changing session or vault configuration");
            string exported = Path.Combine(store.DirectoryPath, "export.json"); store.Export(exported);
            Check(!File.ReadAllText(exported).Contains("FontFamily"), $"{portable} session export excludes appearance preferences");
            byte[] before = File.ReadAllBytes(path);
            using (var occupied = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false; try { service.Save(new()); } catch (IOException) { failed = true; }
                Check(failed && service.Current == expected && before.SequenceEqual(File.ReadAllBytes(path)),
                    $"{portable} failed preference replacement preserves disk and active defaults");
            }
            service.Save(new());
            Check(File.ReadAllBytes(path + ".bak").SequenceEqual(before), $"{portable} preference replacement retains the previous file as backup");
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"Terminal\":{\"FontSize\":13}}"); restarted.Load();
            Check(restarted.Current == new TerminalPreferences { FontSize = 13 } && !restarted.LoadFailed,
                $"{portable} omitted preference fields retain their compatible defaults");
            foreach (string broken in new[] { "{broken", "{\"SchemaVersion\":99,\"Terminal\":{}}", "{\"SchemaVersion\":1,\"Terminal\":{\"FontSize\":999}}" })
            {
                File.WriteAllText(path, broken); restarted.Load();
                bool refused = false; try { restarted.Save(new()); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { refused = true; }
                Check(restarted.LoadFailed && restarted.Current == new TerminalPreferences() && refused && File.ReadAllText(path) == broken,
                    $"{portable} corrupt unsupported or invalid preferences fall back and are preserved on save");
            }
        }
        var screen = new TerminalScreen(20, 4);
        screen.SetDefaultColors(Color.Beige, Color.Navy);
        screen.Feed("\x1b[31mR\x1b[0mA\x1b[32mG\x1b[39;49mB");
        Check(screen.Lines[0][0].Foreground != Color.Beige && screen.Lines[0][1].Foreground == Color.Beige
            && screen.Lines[0][3].Foreground == Color.Beige && screen.Lines[0][3].Background == Color.Navy,
            "ANSI resets restore configured terminal defaults while explicit ANSI colors remain available");
        screen.Feed("\u001bcC");
        Check(screen.Lines[0][0].Text == "C" && screen.Lines[0][0].Foreground == Color.Beige && screen.Lines[0][0].Background == Color.Navy,
            "terminal reset retains the configured foreground and background");
    }

    static void TerminalPreferencesUi(string root, string screenshot)
    {
        using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "preferences-ui"), true));
        var service = new TerminalPreferencesService(new TerminalPreferencesRepository(store.Paths)); service.Load();
        var expected = new TerminalPreferences { FontFamily = "font-not-installed-portharbor", FontSize = 15, ForegroundRgb = 0xEEEEDD, BackgroundRgb = 0x223344, CopyOnSelection = true };
        service.Save(expected);
        using (var defaults = new TerminalView(new TerminalPreferences()))
        {
            var original = defaults.Font;
            defaults.ApplyPreferences(new()); defaults.ApplyPreferences(new());
            Check(ReferenceEquals(original, defaults.Font) && TextRenderer.MeasureText("terminal", defaults.Font).Width > 0,
                "reapplying equal default fonts retains a live font instance for WinForms rendering");
        }
        using (var terminal = new TerminalView(expected))
            Check(terminal.Font.Name != expected.FontFamily && terminal.Font.Size == 15 && terminal.CopyOnSelection
                && !terminal.PasteOnRightClick && (terminal.BackColor.ToArgb() & 0xFFFFFF) == expected.BackgroundRgb,
                "missing terminal font falls back while retaining size colors and behavior");
        using var owner = new Form(); owner.Show(); Application.DoEvents();
        void ShowDialog(Action<TerminalPreferencesDialog> interact)
        {
            using var dialog = new TerminalPreferencesDialog(service);
            Exception? failure = null;
            using var timer = new System.Windows.Forms.Timer { Interval = 80 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { interact(dialog); } catch (Exception error) { failure = error; }
                finally { if (!dialog.IsDisposed && dialog.Visible) dialog.Close(); }
            };
            timer.Start(); dialog.ShowDialog(owner); if (failure != null) throw failure;
        }
        Button Button(Form dialog, string name) => Descendants(dialog).OfType<Button>().Single(b => b.Name == name);
        ShowDialog(dialog =>
        {
            Button(dialog, "Reset").PerformClick(); Button(dialog, "Cancel").PerformClick();
        });
        Check(service.Current == expected && new TerminalPreferencesRepository(store.Paths).Load() == expected,
            "reset followed by cancel discards the preference draft without touching saved defaults");
        ShowDialog(dialog =>
        {
            Descendants(dialog).OfType<NumericUpDown>().Single().Value = 17;
            Descendants(dialog).OfType<CheckBox>().Single(c => c.Name == "PasteOnRightClick").Checked = true;
            using var bitmap = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(screenshot);
            Button(dialog, "Save").PerformClick();
        });
        Check(service.Current.FontSize == 17 && service.Current.PasteOnRightClick && new TerminalPreferencesRepository(store.Paths).Load() == service.Current,
            "terminal settings save the reviewed draft and restore it through a fresh repository");
        using (var blocker = new FileStream(Path.Combine(store.DirectoryPath, "terminal-preferences.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            ShowDialog(dialog =>
            {
                Descendants(dialog).OfType<NumericUpDown>().Single().Value = 18; Button(dialog, "Save").PerformClick();
                Check(dialog.Visible && Descendants(dialog).OfType<Label>().Single(c => c.Name == "SettingsMessage").Text.Contains("未保存") && service.Current.FontSize == 17,
                    "settings save failure keeps the dialog open and does not publish unsaved defaults");
                dialog.Size = dialog.MinimumSize; dialog.PerformLayout(); Application.DoEvents();
                var save = Button(dialog, "Save"); var bounds = dialog.RectangleToClient(save.RectangleToScreen(save.ClientRectangle));
                Check(dialog.ClientRectangle.Contains(bounds), "save action stays visible at the minimum dialog size even after a save error");
            });
        using var form = new MainForm(store, new ConfigurationService(store, store, store), (s, _, _) => new UnreportedTerminalFailure());
        form.Show(); Application.DoEvents();
        form.Connect(new Session { Host = "fixture.invalid" }, "");
        var tabs = FindControl<TabControl>(form); var first = FindControl<TerminalView>(tabs.SelectedTab!);
        Check(first.Font.Size == 17 && first.PasteOnRightClick, "a main window terminal loads persisted global defaults");
        first.ApplyPreferences(new TerminalPreferences { FontSize = 20 });
        var settingsMenu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(m => m.Text == "设置");
        var settingItem = settingsMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(m => m.Text == "终端默认设置…");
        using var dialogTimer = new System.Windows.Forms.Timer { Interval = 50 };
        dialogTimer.Tick += (_, _) =>
        {
            dialogTimer.Stop(); var dialog = Application.OpenForms.OfType<TerminalPreferencesDialog>().Single();
            Descendants(dialog).OfType<NumericUpDown>().Single().Value = 12; Button(dialog, "Save").PerformClick();
        };
        dialogTimer.Start(); settingItem.PerformClick();
        form.Connect(new Session { Host = "fixture.invalid" }, "");
        var second = FindControl<TerminalView>(tabs.SelectedTab!);
        Check(first.Font.Size == 20 && second.Font.Size == 12 && new TerminalPreferencesRepository(store.Paths).Load().FontSize == 12,
            "global changes affect new tabs while an existing tab retains its temporary override");
        foreach (TabPage page in tabs.TabPages) page.Tag = null;
        form.Close(); PumpUntil(() => form.IsDisposed);
        ShowDialog(dialog => { Button(dialog, "Reset").PerformClick(); Button(dialog, "Save").PerformClick(); });
        Check(new TerminalPreferencesRepository(store.Paths).Load() == new TerminalPreferences(), "reset defaults becomes persistent only after saving");
        string path = Path.Combine(store.DirectoryPath, "terminal-preferences.json"); File.WriteAllText(path, "{broken"); service.Load();
        ShowDialog(dialog =>
        {
            var message = Descendants(dialog).OfType<Label>().Single(c => c.Name == "SettingsMessage");
            bool warned = message.Text.Contains("原文件已保留"); Button(dialog, "Save").PerformClick();
            Check(warned && dialog.Visible && message.Text.Contains("未保存") && File.ReadAllText(path) == "{broken",
                "corrupt preferences show recovery guidance and saving cannot overwrite the preserved file");
        });
        owner.Close();
    }
}
