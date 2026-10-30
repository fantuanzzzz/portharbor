namespace PortHarbor;

internal sealed class TerminalPreferencesDialog : Form
{
    readonly TerminalPreferencesService service;
    readonly TerminalView preview = new() { Dock = DockStyle.Fill, MinimumSize = new Size(320, 110) };
    readonly ComboBox family = new() { Name = "FontFamily", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
    readonly NumericUpDown size = new() { Name = "FontSize", Minimum = 6, Maximum = 48, DecimalPlaces = 1, Increment = .5m, Width = 90 };
    readonly CheckBox bold = new() { Text = "粗体", AutoSize = true }, italic = new() { Text = "斜体", AutoSize = true };
    readonly CheckBox copy = new() { Name = "CopyOnSelection", Text = "左键选取后自动复制", AutoSize = true };
    readonly CheckBox paste = new() { Name = "PasteOnRightClick", Text = "右键直接粘贴", AutoSize = true };
    readonly Button foreground = new UiButton { Name = "Foreground", AutoSize = true }, background = new UiButton { Name = "Background", AutoSize = true };
    readonly Label message = new() { Name = "SettingsMessage", AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(560, 0) };
    int foregroundRgb, backgroundRgb, additionalStyle;
    bool updating;
    internal TerminalPreferencesDialog(TerminalPreferencesService service)
    {
        this.service = service;
        Text = "终端默认设置"; ClientSize = new Size(620, 530); MinimumSize = new Size(580, 550);
        StartPosition = FormStartPosition.CenterParent; UiTheme.AttachDialog(this);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
        layout.SizeChanged += (_, _) => message.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal), 0);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Wide(Control control, int row) { layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 2); }
        Wide(new Label { Text = "保存为全局默认：仅影响以后新开的终端标签，重启后保留。\n当前标签的右键菜单调整不会保存为默认。", AutoSize = true, Margin = new Padding(0, 0, 0, 14) }, 0);
        layout.Controls.Add(new Label { Text = "字体（留空自动选择等宽字体）", AutoSize = true }, 0, 1); layout.Controls.Add(family, 1, 1);
        family.Items.AddRange(TerminalView.InstalledFontNames().OrderBy(n => n).ToArray());
        layout.Controls.Add(new Label { Text = "字号 / 样式", AutoSize = true }, 0, 2);
        var fontOptions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; fontOptions.Controls.AddRange([size, bold, italic]); layout.Controls.Add(fontOptions, 1, 2);
        var colors = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; colors.Controls.AddRange([foreground, background]);
        layout.Controls.Add(new Label { Text = "默认颜色", AutoSize = true }, 0, 3); layout.Controls.Add(colors, 1, 3);
        var behavior = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; behavior.Controls.AddRange([copy, paste]); Wide(behavior, 4);
        Wide(preview, 5); layout.RowStyles.Add(new RowStyle()); layout.RowStyles.Add(new RowStyle()); layout.RowStyles.Add(new RowStyle()); layout.RowStyles.Add(new RowStyle()); layout.RowStyles.Add(new RowStyle()); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Wide(new Label { Name = "FontNotice", Text = "找不到所填字体时自动回退为等宽字体；多行粘贴仍需确认。", AutoSize = true, Margin = new Padding(0, 8, 0, 4) }, 6);
        Wide(message, 7);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(18, 8, 18, 18), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new UiButton { Name = "Cancel", Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new UiButton { Name = "Save", Text = "保存默认", AutoSize = true, Kind = UiButtonKind.Primary };
        var reset = new UiButton { Name = "Reset", Text = "恢复默认", AutoSize = true };
        actions.Controls.AddRange([cancel, save, reset]); Controls.Add(layout); Controls.Add(actions);
        UiTheme.Role(message, UiRole.Error);
        CancelButton = cancel; AcceptButton = save;
        family.TextChanged += (_, _) => UpdatePreview(); size.ValueChanged += (_, _) => UpdatePreview();
        foreach (var check in new[] { bold, italic, copy, paste }) check.CheckedChanged += (_, _) => UpdatePreview();
        foreground.Click += (_, _) => PickColor(true); background.Click += (_, _) => PickColor(false);
        reset.Click += (_, _) => Populate(new());
        save.Click += (_, _) =>
        {
            try { service.Save(Read()); DialogResult = DialogResult.OK; Close(); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { message.Text = "未保存。请检查配置目录权限及 terminal-preferences.json；损坏或不支持的原文件需先移走并保管，再重试。"; }
        };
        Populate(service.Current);
        if (service.LoadFailed) message.Text = "偏好未能读取，暂用默认值。原文件已保留；保存前请检查配置目录中的 terminal-preferences.json。";
    }
    void Populate(TerminalPreferences value)
    {
        updating = true;
        family.Text = value.FontFamily; size.Value = (decimal)value.FontSize;
        bold.Checked = (value.FontStyle & 1) != 0; italic.Checked = (value.FontStyle & 2) != 0; additionalStyle = value.FontStyle & 12;
        foregroundRgb = value.ForegroundRgb; backgroundRgb = value.BackgroundRgb;
        copy.Checked = value.CopyOnSelection; paste.Checked = value.PasteOnRightClick;
        updating = false; UpdatePreview();
    }
    TerminalPreferences Read() => new() { FontFamily = family.Text.Trim(), FontSize = (float)size.Value,
        FontStyle = additionalStyle | (bold.Checked ? 1 : 0) | (italic.Checked ? 2 : 0), ForegroundRgb = foregroundRgb,
        BackgroundRgb = backgroundRgb, CopyOnSelection = copy.Checked, PasteOnRightClick = paste.Checked };
    void UpdatePreview()
    {
        if (updating) return;
        try
        {
            preview.ApplyPreferences(Read());
            foreground.Text = $"文字 #{foregroundRgb:X6}"; background.Text = $"背景 #{backgroundRgb:X6}";
        }
        catch (ArgumentException) { message.Text = "字体名称或参数无效。"; }
    }
    void PickColor(bool text)
    {
        using var dialog = new ColorDialog { FullOpen = true, Color = Color.FromArgb(unchecked((int)0xFF000000) | (text ? foregroundRgb : backgroundRgb)) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (text) foregroundRgb = dialog.Color.ToArgb() & 0xFFFFFF; else backgroundRgb = dialog.Color.ToArgb() & 0xFFFFFF;
        UpdatePreview();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); preview.Append("PortHarbor 终端预览\r\n中文 / English 0123456789\r\n\x1b[32m成功\x1b[0m · 默认文字\r\n");
    }
}
