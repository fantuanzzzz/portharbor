using System.IO.Ports;

namespace PortHarbor;

public sealed class SessionEditor : Form
{
    public Session Result { get; private set; }
    public string Password => password.Text;
    public bool ConnectNow { get; private set; }
    public bool Persist => persist.Checked;
    readonly Session original;
    readonly TableLayoutPanel fields = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(18), GrowStyle = TableLayoutPanelGrowStyle.AddRows };
    readonly Dictionary<string, Control> inputs = [];
    readonly Dictionary<string, (Control label, Control field)> rows = [];
    readonly ComboBox protocol = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    readonly TextBox password = new() { UseSystemPasswordChar = true };
    readonly CheckBox savePassword = new() { Text = "保存密码（主密码 + Windows 用户加密）", AutoSize = true };
    readonly CheckBox persist = new() { Text = "保存此会话", Checked = true, AutoSize = true };
    public SessionEditor(Session? session = null, string secret = "", IEnumerable<string>? groups = null)
    {
        original = session ?? new(); Result = original with { };
        Text = session == null ? "新建连接" : "编辑会话";
        Font = new Font("Microsoft YaHei UI", 9); AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(650, 770); MinimumSize = new Size(570, 500); StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(247, 249, 252);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(fields);
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddText("Name", "会话名称", original.Name);
        var group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        group.Items.AddRange((groups ?? []).Append("默认").Append(original.Group).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).Cast<object>().ToArray());
        group.Text = original.Group; Add("Group", "分类", group);
        protocol.Items.AddRange(Enum.GetNames<Protocol>()); protocol.SelectedItem = original.Protocol.ToString(); Add("Protocol", "连接类型", protocol);
        AddText("Host", "主机 / IP", original.Host); AddNumber("Port", "端口", original.Port, 1, 65535);
        AddText("Username", "用户名 / 域\\用户名", original.Username);
        password.Text = secret; Add("Password", "密码 / 私钥口令", password); savePassword.Checked = original.SavePassword; Add("SavePassword", "", savePassword);
        AddText("PrivateKey", "SSH 私钥路径（可选）", original.PrivateKey);
        AddText("RemotePath", "初始远程目录", original.RemotePath);
        AddCheck("UseTls", "FTP 显式 TLS", original.UseTls);
        var ports = new ComboBox { Width = 320 }; ports.Items.AddRange(SerialPort.GetPortNames()); ports.Text = original.SerialPort; Add("SerialPort", "串口", ports);
        AddNumber("BaudRate", "波特率", original.BaudRate, 1, 4000000); AddNumber("DataBits", "数据位", original.DataBits, 5, 8);
        AddChoice("Parity", "校验", Enum.GetNames<Parity>(), original.Parity); AddChoice("StopBits", "停止位", ["One", "OnePointFive", "Two"], original.StopBits); AddChoice("Handshake", "流控", Enum.GetNames<Handshake>(), original.Handshake);
        AddText("Executable", "Shell 程序", original.Executable); AddText("Arguments", "启动参数", original.Arguments); AddText("WorkingDirectory", "工作目录（可选）", original.WorkingDirectory);
        AddCheck("RdpFullScreen", "RDP 全屏", original.RdpFullScreen); AddCheck("Clipboard", "RDP 剪贴板共享", original.Clipboard);
        Add("Persist", "", persist);
        var note = new Label { Text = "终端使用 UTF-8。SSH 私钥可留空；填写私钥后，密码框用作私钥口令。\nRDP 在 Windows 远程桌面窗口打开，登录遵循系统凭据策略。", AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = Color.DimGray };
        Add("Note", "", note);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var connect = new Button { Text = "保存并连接", AutoSize = true, BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var save = new Button { Text = "仅保存", AutoSize = true };
        connect.Click += (_, _) => Finish(true); save.Click += (_, _) => { persist.Checked = true; Finish(false); };
        persist.CheckedChanged += (_, _) => connect.Text = persist.Checked ? "保存并连接" : "连接";
        buttons.Controls.AddRange([cancel, connect, save]); AcceptButton = connect; CancelButton = cancel;
        Controls.Add(scroll); Controls.Add(buttons);
        protocol.SelectedIndexChanged += (_, _) => { ((NumericUpDown)inputs["Port"]).Value = Session.DefaultPort(SelectedProtocol); UpdateFields(); };
        UpdateFields();
    }
    Protocol SelectedProtocol => Enum.Parse<Protocol>(protocol.Text);
    void Add(string key, string label, Control control)
    {
        int index = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 0, 0), Margin = new Padding(0, 0, 8, 8) };
        control.Dock = DockStyle.Top; control.Margin = new Padding(0, 4, 0, 8);
        fields.Controls.Add(title, 0, index); fields.Controls.Add(control, 1, index); inputs[key] = control; rows[key] = (title, control);
    }
    void AddText(string key, string title, string value) => Add(key, title, new TextBox { Text = value });
    void AddNumber(string key, string title, int value, int min, int max) => Add(key, title, new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max) });
    void AddCheck(string key, string title, bool value) => Add(key, "", new CheckBox { Text = title, Checked = value, AutoSize = true });
    void AddChoice(string key, string title, string[] values, string value) { var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; box.Items.AddRange(values); box.SelectedItem = value; if (box.SelectedIndex < 0) box.SelectedIndex = 0; Add(key, title, box); }
    void UpdateFields()
    {
        var p = SelectedProtocol;
        foreach (var (key, pair) in rows)
        {
            bool visible = key switch {
                "Host" or "Port" => p is not (Protocol.Serial or Protocol.Shell),
                "Username" or "Password" or "SavePassword" => p is Protocol.SSH or Protocol.SFTP or Protocol.FTP or Protocol.RDP,
                "PrivateKey" => p is Protocol.SSH or Protocol.SFTP,
                "RemotePath" => p is Protocol.FTP or Protocol.SFTP,
                "UseTls" => p == Protocol.FTP,
                "SerialPort" or "BaudRate" or "DataBits" or "Parity" or "StopBits" or "Handshake" => p == Protocol.Serial,
                "Executable" or "Arguments" or "WorkingDirectory" => p == Protocol.Shell,
                "RdpFullScreen" or "Clipboard" => p == Protocol.RDP,
                _ => true };
            pair.label.Visible = pair.field.Visible = visible;
        }
    }
    void Finish(bool connect)
    {
        string S(string key) => inputs[key].Text.Trim(); int N(string key) => (int)((NumericUpDown)inputs[key]).Value; bool B(string key) => ((CheckBox)inputs[key]).Checked;
        try
        {
            Result = original with { Name = S("Name"), Group = S("Group"), Protocol = SelectedProtocol, Host = S("Host"), Port = N("Port"), Username = S("Username"), PrivateKey = S("PrivateKey"), RemotePath = S("RemotePath"), UseTls = B("UseTls"), SerialPort = S("SerialPort"), BaudRate = N("BaudRate"), DataBits = N("DataBits"), Parity = S("Parity"), StopBits = S("StopBits"), Handshake = S("Handshake"), Executable = S("Executable"), Arguments = inputs["Arguments"].Text, WorkingDirectory = S("WorkingDirectory"), RdpFullScreen = B("RdpFullScreen"), Clipboard = B("Clipboard"), SavePassword = savePassword.Checked && SelectedProtocol is Protocol.SSH or Protocol.SFTP or Protocol.FTP or Protocol.RDP };
            Result.Validate(); ConnectNow = connect; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "检查连接参数", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
}

public static class TextPrompt
{
    public static string? Ask(IWin32Window owner, string title, string label, string initial = "", bool secret = false)
    {
        using var form = new Form { Text = title, Size = new Size(460, 180), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Microsoft YaHei UI", 9) };
        var text = new TextBox { Text = initial, UseSystemPasswordChar = secret, Left = 20, Top = 50, Width = 400 };
        form.Controls.Add(new Label { Text = label, Left = 20, Top = 18, AutoSize = true }); form.Controls.Add(text);
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 250, Top = 88 }; var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 335, Top = 88 };
        form.Controls.AddRange([ok, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? text.Text : null;
    }

    public static string? AskWithSkip(IWin32Window owner, string title, string label, string initial = "", bool secret = false)
    {
        using var form = new Form { Text = title, Size = new Size(460, 180), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Microsoft YaHei UI", 9) };
        var text = new TextBox { Text = initial, UseSystemPasswordChar = secret, Left = 20, Top = 50, Width = 400 };
        form.Controls.Add(new Label { Text = label, Left = 20, Top = 18, AutoSize = true }); form.Controls.Add(text);
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 175, Top = 88 };
        var skip = new Button { Text = "跳过", DialogResult = DialogResult.Ignore, Left = 260, Top = 88 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 345, Top = 88 };
        form.Controls.AddRange([ok, skip, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(owner) switch { DialogResult.OK => text.Text, DialogResult.Ignore => "", _ => null };
    }
}

