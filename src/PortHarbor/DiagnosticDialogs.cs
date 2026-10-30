namespace PortHarbor;

internal static class DiagnosticDialogs
{
    internal static void Preview(IWin32Window owner, FileDiagnosticLog log, IFilePicker picker)
    {
        var metadata = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(typeof(MainForm).Assembly)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
        string? Read(string key) => metadata.GetValueOrDefault("PortHarbor." + key);
        var build = new DiagnosticBuildIdentity(Read("SourceCommit"),
            bool.TryParse(Read("SourceDirty"), out bool dirty) ? dirty : null, Read("SourceSnapshot"), Read("BuildSdk"));
        var preview = DiagnosticBundleService.Preview(typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "unknown",
            Environment.Version.ToString(), Environment.OSVersion.VersionString, log.Snapshot(), build);
        // Freeze and display the exact snapshot that the user will export.
        using var form = new Form { Text = "诊断包预览", Width = 850, Height = 650, StartPosition = FormStartPosition.CenterParent };
        UiTheme.AttachDialog(form);
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Text = DiagnosticBundleService.ToJson(preview)
        };
        var hint = new Label { Dock = DockStyle.Top, Height = 52, Text = "请核对以下内容后导出。仅含版本、平台、操作编号、错误码和计数；不含会话名称、端点、凭据、路径或终端内容。\n导出文件由你自行保管或分享。" };
        var save = new UiButton { Text = "导出以上内容…", Dock = DockStyle.Bottom, Height = 36, Kind = UiButtonKind.Primary };
        save.Click += (_, _) =>
        {
            string? path = picker.SaveFile(new("JSON (*.json)|*.json", "PortHarbor-diagnostics.json"));
            if (path == null) return;
            try { DiagnosticBundleService.Export(path, preview); form.Close(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { MessageBox.Show(form, "无法保存诊断包，请检查目标目录权限。", "诊断导出", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        form.Controls.Add(text); form.Controls.Add(hint); form.Controls.Add(save); form.ShowDialog(owner);
    }

    internal static void Settings(IWin32Window owner, FileDiagnosticLog log)
    {
        var current = log.Settings;
        using var form = new Form { Text = "本地诊断设置", Width = 430, Height = 300, StartPosition = FormStartPosition.CenterParent };
        UiTheme.AttachDialog(form);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(UiTheme.Section), WrapContents = false };
        var enabled = new CheckBox { Text = "记录本地诊断（始终排除端点及敏感内容）", Checked = current.Enabled, AutoSize = true };
        var size = new NumericUpDown { Minimum = 1, Maximum = 16, Value = Math.Clamp(current.MaxBytes / (1024 * 1024), 1, 16) };
        var days = new NumericUpDown { Minimum = 0, Maximum = 365, Value = current.RetentionDays };
        var archives = new NumericUpDown { Minimum = 0, Maximum = 32, Value = current.MaxArchives };
        var save = new UiButton { Text = "保存", AutoSize = true, Kind = UiButtonKind.Primary };
        foreach (Control c in new Control[] { enabled, new Label { Text = "每个文件大小上限（MiB）", AutoSize = true }, size,
            new Label { Text = "归档保留天数 / 最多归档文件数", AutoSize = true }, days, archives, save }) panel.Controls.Add(c);
        save.Click += (_, _) =>
        {
            try { log.Configure(new(enabled.Checked, (long)size.Value * 1024 * 1024, (int)days.Value, (int)archives.Value)); form.Close(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { MessageBox.Show(form, "无法保存诊断设置，请检查数据目录权限。", "诊断设置", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        form.Controls.Add(panel); form.ShowDialog(owner);
    }
}
