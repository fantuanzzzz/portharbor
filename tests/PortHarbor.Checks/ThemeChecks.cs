using System.Drawing;
using System.Reflection;
using PortHarbor;

namespace PortHarbor.Checks;

internal static partial class Program
{
    static void ThemeUi(string root, string output)
    {
        Directory.CreateDirectory(output);
        void Snapshot(Control control, string name)
        {
            using var bitmap = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size)); bitmap.Save(Path.Combine(output, name + ".png"));
        }
        foreach (var choice in Enum.GetValues<InterfaceTheme>())
        {
            var p = UiPalette.Resolve(choice, false);
            double Contrast(Color a, Color b)
            {
                double L(Color c)
                {
                    double C(byte n) { double value = n / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
                    return .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B);
                }
                double x = L(a), y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
            }
            Check(new[] { p.Page, p.Surface, p.Subtle, p.Selected }.All(background => Contrast(p.Text, background) >= 4.5)
                && Contrast(p.Muted, p.Surface) >= 4.5 && Contrast(p.Muted, p.Page) >= 4.5
                && new[] { p.Success, p.Warning, p.Error }.All(c => Contrast(c, p.Surface) >= 4.5)
                && Contrast(p.OnAccent, p.Accent) >= 4.5, $"{choice} interface text and semantic statuses meet a 4.5 contrast ratio");
            var highContrast = UiPalette.Resolve(choice, true);
            Check(highContrast.HighContrast && highContrast.Surface == SystemColors.Window && highContrast.Text == SystemColors.WindowText
                && highContrast.Accent == SystemColors.Highlight, $"{choice} yields to Windows high contrast system colors");
            using var store = new Store(new WindowsSecretProtector(), TestPaths(Path.Combine(root, "theme-" + choice), true));
            using var form = new MainForm(store, new ConfigurationService(store, store, store), (_, _, _) => new UnreportedTerminalFailure());
            form.Show(); Application.DoEvents();
            var menu = form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(m => m.Text == "设置")
                .DropDownItems.OfType<ToolStripMenuItem>().Single(m => m.Name == "InterfaceTheme");
            ((ToolStripMenuItem)menu.DropDownItems["Theme" + choice]!).PerformClick();
            Check(UiTheme.Choice(form) == choice && new WorkspaceLayoutRepository(store.Paths).Load().Theme == choice
                && ((ToolStripMenuItem)menu.DropDownItems["Theme" + choice]!).Checked, $"{choice} menu applies and saves the selected interface theme");
            Snapshot(form, "theme-" + choice + "-main");
            form.Connect(new Session { Host = "fixture.invalid", Name = "主题检查" }, ""); Application.DoEvents();
            var tabs = FindControl<TabControl>(form); var terminal = FindControl<TerminalView>(tabs.SelectedTab!);
            terminal.ApplyPreferences(new TerminalPreferences { FontSize = 14, ForegroundRgb = 0xFEEDCB, BackgroundRgb = 0x123456 });
            terminal.Append("终端配色 / English\r\n"); Application.DoEvents();
            var font = terminal.Font; var foreground = terminal.ForeColor; var backgroundColor = terminal.BackColor;
            form.SetInterfaceTheme(choice == InterfaceTheme.Light ? InterfaceTheme.Dark : InterfaceTheme.Light);
            Check(terminal.Font == font && terminal.ForeColor == foreground && terminal.BackColor == backgroundColor,
                $"{choice} theme switch preserves the active terminal font and custom colors");
            form.SetInterfaceTheme(choice);
            void InspectModal(Action open, Action<Form> inspect)
            {
                Exception? failure = null;
                using var timer = new System.Windows.Forms.Timer { Interval = 40 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop(); var dialog = Application.OpenForms.Cast<Form>().Single(f => f.Modal);
                    try { inspect(dialog); }
                    catch (Exception error) { failure = error; }
                    finally { dialog.Close(); }
                };
                timer.Start(); open(); if (failure != null) throw failure;
            }
            using (var log = new FileDiagnosticLog(Path.Combine(root, "theme-log-" + choice)))
            {
                InspectModal(() => DiagnosticDialogs.Settings(form, log), dialog =>
                {
                    var panel = FindControl<FlowLayoutPanel>(dialog); var save = FindControl<UiButton>(dialog);
                    panel.ScrollControlIntoView(save); Application.DoEvents();
                    Check(UiTheme.Choice(dialog) == choice && save.Bottom <= panel.ClientSize.Height,
                        $"{choice} diagnostic settings inherit theme and allow reaching save at a small height");
                    Snapshot(dialog, "theme-" + choice + "-diagnostic-settings");
                });
                InspectModal(() => DiagnosticDialogs.Preview(form, log, new FixtureFilePicker()), dialog =>
                {
                    Check(UiTheme.Choice(dialog) == choice && FindControl<TextBox>(dialog).BackColor == p.Surface,
                        $"{choice} diagnostic preview inherits theme and uses a readable input surface");
                    Snapshot(dialog, "theme-" + choice + "-diagnostic-preview");
                });
            }
            InspectModal(() => TextPrompt.AskWithSkip(form, "输入提示", "请输入口令", secret: true), dialog =>
            {
                Check(UiTheme.Choice(dialog) == choice && FindControl<TextBox>(dialog).UseSystemPasswordChar
                    && dialog.Controls.OfType<UiButton>().All(b => dialog.ClientRectangle.Contains(b.Bounds)),
                    $"{choice} secret prompt keeps masked input and visible keyboard-accessible actions");
                Snapshot(dialog, "theme-" + choice + "-prompt");
            });
            using (var editor = new SessionEditor(new Session { Host = "example.invalid" }))
            {
                editor.Show(form); Application.DoEvents();
                Check(UiTheme.Choice(editor) == choice && Descendants(editor).OfType<UiButton>().Count(b => b.Kind == UiButtonKind.Primary) == 1,
                    $"{choice} session editor inherits theme and has one primary submit action");
                Snapshot(editor, "theme-" + choice + "-editor"); editor.Close();
            }
            using (var licenses = new ThirdPartyLicensesDialog())
            {
                licenses.Show(form); Application.DoEvents();
                string notices = FindControl<TextBox>(licenses).Text;
                Check(UiTheme.Choice(licenses) == choice && notices.Contains("SSH.NET")
                    && notices.Contains("FluentFTP") && notices.Contains(".NET Foundation")
                    && notices.Contains("Permission is hereby granted"),
                    $"{choice} embedded license viewer inherits theme and includes dependency and runtime licenses");
                Snapshot(licenses, "theme-" + choice + "-licenses"); licenses.Close();
            }
            var backend = new MemoryRemoteFileSystem(); backend.Files["/中文文件.txt"] = [1, 2, 3];
            using (var filesHost = new Form { Size = new Size(960, 640), Font = UiTheme.BodyFont })
            {
                var files = new FileView(new FileConnection(Protocol.SFTP, backend), "/") { Dock = DockStyle.Fill };
                filesHost.Controls.Add(files); UiTheme.Apply(filesHost, choice); filesHost.Show(form); Application.DoEvents();
                var list = FindControl<ListView>(filesHost); PumpUntil(() => list.Items.Count == 1);
                list.Items[0].Selected = true; list.Focus();
                Check(list.OwnerDraw && list.BackColor == UiTheme.Palette(filesHost).Surface
                    && Descendants(files).OfType<UiButton>().Single(b => b.Text == "删除").Kind == UiButtonKind.Danger,
                    $"{choice} file list selection headers and dangerous action use shared semantic styles");
                Snapshot(filesHost, "theme-" + choice + "-files");
                filesHost.Size = new Size(700, 500); Application.DoEvents();
                var toolbar = files.Controls.OfType<FlowLayoutPanel>().Single();
                Check(toolbar.Controls.OfType<Button>().All(b => b.Bottom <= toolbar.ClientSize.Height),
                    $"{choice} file toolbar keeps wrapped actions visible at a narrower width");
                filesHost.Close();
            }
            var prefs = new TerminalPreferencesService(new TerminalPreferencesRepository(store.Paths)); prefs.Load();
            using (var dialog = new TerminalPreferencesDialog(prefs))
            {
                dialog.Show(form); Application.DoEvents();
                var preview = FindControl<TerminalView>(dialog);
                Check(UiTheme.Choice(dialog) == choice && preview.BackColor == TerminalScreen.DefaultBackground,
                    $"{choice} terminal preference dialog inherits interface theme while its preview retains terminal defaults");
                Snapshot(dialog, "theme-" + choice + "-preferences"); dialog.Close();
            }
            using (var gallery = new Form { ClientSize = new Size(740, 430), Font = UiTheme.BodyFont, Text = "P03 按钮与状态样本" })
            {
                var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), FlowDirection = FlowDirection.TopDown, WrapContents = false };
                var primary = new UiButton { Text = "主要操作", Kind = UiButtonKind.Primary, AutoSize = true };
                var normal = new UiButton { Text = "普通操作", AutoSize = true };
                var disabled = new UiButton { Text = "不可用", Enabled = false, AutoSize = true };
                var danger = new UiButton { Text = "删除", Kind = UiButtonKind.Danger, AutoSize = true };
                panel.Controls.AddRange([primary, normal, disabled, danger]);
                foreach (var (text, role) in new[] { ("已连接", UiRole.Success), ("请核对操作结果", UiRole.Warning), ("身份验证失败", UiRole.Error) })
                { var label = new Label { Text = text, AutoSize = true }; UiTheme.Role(label, role); panel.Controls.Add(label); }
                gallery.Controls.Add(panel); UiTheme.Apply(gallery, choice); gallery.Show(form); primary.Focus(); Application.DoEvents();
                Snapshot(gallery, "theme-" + choice + "-states");
                using var original = new Bitmap(normal.Width, normal.Height); normal.DrawToBitmap(original, new Rectangle(Point.Empty, normal.Size));
                typeof(UiButton).GetMethod("OnMouseEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(normal, [EventArgs.Empty]);
                using var hovered = new Bitmap(normal.Width, normal.Height); normal.DrawToBitmap(hovered, new Rectangle(Point.Empty, normal.Size));
                Check(original.GetPixel(8, 8) != hovered.GetPixel(8, 8), $"{choice} button hover has visible feedback");
                Snapshot(gallery, "theme-" + choice + "-hover");
                typeof(UiButton).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(primary, [new MouseEventArgs(MouseButtons.Left, 1, 8, 8, 0)]);
                Snapshot(gallery, "theme-" + choice + "-pressed");
                primary.ApplyPalette(highContrast);
                Check(primary.FlatStyle == FlatStyle.Standard && primary.ForeColor == SystemColors.ControlText,
                    $"{choice} high contrast button uses native rendering and system text");
                gallery.Close();
            }
            var status = FindControl<ConnectionStatusLabel>(tabs.SelectedTab!);
            status.Report(new(Guid.NewGuid(), Protocol.SSH, ConnectionState.Failed, ConnectionStage.Authenticating, ConnectionErrorCode.AuthenticationFailed, false, ""));
            Check(status.Text.Contains("身份验证失败") && status.ForeColor == UiTheme.Palette(form).Error,
                $"{choice} connection failures retain readable error text and shared semantic color");
            foreach (TabPage page in tabs.TabPages) page.Tag = null;
            form.Close(); PumpUntil(() => form.IsDisposed);
            using var restarted = new MainForm(store); restarted.Show(); Application.DoEvents();
            Check(UiTheme.Choice(restarted) == choice, $"{choice} interface theme survives main window restart");
            restarted.ResetWorkspaceLayout();
            Check(UiTheme.Choice(restarted) == choice && new WorkspaceLayoutRepository(store.Paths).Load().Theme == choice,
                $"{choice} resetting workspace geometry retains the chosen interface theme");
            string file = Path.Combine(store.DirectoryPath, "workspace-layout.json"); var bytes = File.ReadAllBytes(file);
            using (var occupied = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                restarted.SetInterfaceTheme(choice == InterfaceTheme.Light ? InterfaceTheme.Dark : InterfaceTheme.Light);
                Check(bytes.SequenceEqual(File.ReadAllBytes(file)) && restarted.Controls.OfType<StatusStrip>().Single().Items["LayoutMessage"]!.Text!.Contains("未保存"),
                    $"{choice} failed theme persistence preserves disk data and visibly reports that settings were not saved");
            }
            restarted.Close(); PumpUntil(() => restarted.IsDisposed);
        }
    }
}
