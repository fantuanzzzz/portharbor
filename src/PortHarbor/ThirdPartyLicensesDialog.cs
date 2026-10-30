namespace PortHarbor;

internal sealed class ThirdPartyLicensesDialog : Form
{
    internal static string ReadNotices()
    {
        var assembly = typeof(ThirdPartyLicensesDialog).Assembly;
        return string.Join("\r\n\r\n", assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("PortHarbor.Licenses.", StringComparison.Ordinal))
            .OrderBy(n => n.EndsWith("NOTICES.md", StringComparison.Ordinal) ? "" : n, StringComparer.Ordinal)
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return n["PortHarbor.Licenses.".Length..] + "\r\n\r\n" + reader.ReadToEnd();
            }));
    }

    internal ThirdPartyLicensesDialog()
    {
        Text = "第三方许可"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 620); MinimumSize = new Size(520, 360);
        AutoScaleMode = AutoScaleMode.Dpi; Font = UiTheme.BodyFont;
        var notices = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical, WordWrap = true, AccessibleName = "第三方许可文本", Text = ReadNotices() };
        var close = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        actions.Controls.Add(close); Controls.Add(notices); Controls.Add(actions); CancelButton = close;
        Shown += (_, _) => { notices.Select(0, 0); close.Focus(); };
        UiTheme.AttachDialog(this);
    }
}
