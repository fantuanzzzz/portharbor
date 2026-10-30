namespace PortHarbor;

public sealed class SessionEditor : Form
{
    public Session Result { get; private set; }
    public string Password => password.Text;
    public bool ConnectNow { get; private set; }
    public bool Persist { get; private set; }
    readonly Session original;
    readonly TableLayoutPanel fields = CreateTable("EditorFields");
    readonly TableLayoutPanel advanced = CreateTable("AdvancedFields");
    readonly Panel scroll = new() { Name = "EditorScroll", Dock = DockStyle.Fill, AutoScroll = true };
    readonly Dictionary<string, Control> inputs = [];
    readonly Dictionary<string, (Control label, Control field)> rows = [];
    readonly Dictionary<string, Label> errors = [];
    readonly Dictionary<Protocol, int> portsByProtocol = [];
    readonly ComboBox protocol = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox password = new() { UseSystemPasswordChar = true };
    readonly CheckBox showPassword = new() { Name = "ShowPassword", Text = "显示密码 / 口令", AutoSize = true };
    readonly CheckBox savePassword = new() { Text = "保存密码（需要主密码；安装模式另加 Windows 用户保护）", AutoSize = true };
    readonly CheckBox expandAdvanced = new() { Name = "ExpandAdvanced", Text = "高级设置（可选）", AutoSize = true };
    readonly PlatformConnectionPolicy platform;
    readonly IFilePicker filePicker;
    readonly IFolderPicker folderPicker;
    readonly Label availability = new() { AutoSize = true };
    readonly Label serialNotice = new() { AutoSize = true };
    readonly Label feedback = new() { Name = "EditorFeedback", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(12, 4, 12, 0) };
    readonly Button connect = new UiButton { Name = "SaveAndConnect", Text = "保存并连接", AutoSize = true, Kind = UiButtonKind.Primary };
    readonly Button connectOnly = new UiButton { Name = "ConnectOnly", Text = "仅连接", AutoSize = true };
    readonly HashSet<string> advancedKeys = ["PrivateKey", "RemotePath", "UseTls", "DataBits", "Parity", "StopBits", "Handshake", "Arguments", "WorkingDirectory", "RdpFullScreen", "Clipboard"];
    Protocol lastProtocol;
    bool validationStarted;

    public SessionEditor(Session? session = null, string secret = "", IEnumerable<string>? groups = null, ISerialPortProvider? serialPorts = null,
        IPlatformCapabilities? capabilities = null, IFilePicker? filePicker = null, IFolderPicker? folderPicker = null, bool creating = false)
    {
        original = (session ?? new() { Name = "" }) with { }; Result = original with { };
        var windowsPicker = new WindowsFilePicker(this);
        this.filePicker = filePicker ?? windowsPicker; this.folderPicker = folderPicker ?? windowsPicker;
        serialPorts ??= new WindowsSerialPortProvider();
        platform = new PlatformConnectionPolicy(capabilities ?? new WindowsPlatformCapabilities());
        Text = session == null || creating ? "新建连接" : "编辑会话";
        UiTheme.AttachDialog(this);
        Size = new Size(720, 790); MinimumSize = new Size(570, 500); StartPosition = FormStartPosition.CenterParent;
        fields.Padding = new Padding(18); advanced.Padding = new Padding(0, 4, 0, 0);
        scroll.Controls.Add(fields);
        AddHeading("基本设置", "BasicHeading");
        AddText("Name", "会话名称（可选）", original.Name);
        var group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
        group.Items.AddRange((groups ?? []).Append("默认").Append(original.Group).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).Cast<object>().ToArray());
        group.Text = original.Group; Add("Group", "分类路径", group);
        protocol.Items.AddRange(Enum.GetNames<Protocol>()); protocol.SelectedItem = original.Protocol.ToString(); Add("Protocol", "连接类型", protocol);
        Add("PlatformAvailability", "", availability);
        AddText("Host", "主机 / IP", original.Host); AddNumber("Port", "端口", original.Port, 1, 65535);
        AddText("Username", "用户名", original.Username);
        password.Text = secret; Add("Password", "密码 / 私钥口令", password);
        Add("ShowPassword", "", showPassword);
        savePassword.Checked = original.SavePassword; Add("SavePassword", "", savePassword);
        AddPath("PrivateKey", "SSH 私钥路径", original.PrivateKey, "选择私钥…", () => this.filePicker.OpenFile(new("私钥文件 (*.*)|*.*", inputs["PrivateKey"].Text)));
        AddText("RemotePath", "初始远程目录", original.RemotePath);
        AddCheck("UseTls", "FTP 显式 TLS", original.UseTls);
        var ports = new ComboBox();
        try
        {
            var names = serialPorts.GetPortNames(); ports.Items.AddRange(names.Cast<object>().ToArray());
            if (names.Count == 0) serialNotice.Text = "未检测到串口，可连接设备后重新打开此窗口，或手动输入端口名称。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        { serialNotice.Text = "无法读取串口列表，请检查设备或权限；仍可手动输入端口名称。"; }
        ports.Text = original.SerialPort; Add("SerialPort", "串口", ports); Add("SerialNotice", "", serialNotice);
        AddNumber("BaudRate", "波特率", original.BaudRate, 1, 4000000); AddNumber("DataBits", "数据位", original.DataBits, 5, 8);
        AddChoice("Parity", "校验", serialPorts.ParityOptions.ToArray(), original.Parity); AddChoice("StopBits", "停止位", serialPorts.StopBitOptions.ToArray(), original.StopBits); AddChoice("Handshake", "流控", serialPorts.HandshakeOptions.ToArray(), original.Handshake);
        AddPath("Executable", "Shell 程序", original.Executable, "选择程序…", () => this.filePicker.OpenFile(new("程序 (*.exe;*.com)|*.exe;*.com|所有文件 (*.*)|*.*", inputs["Executable"].Text)));
        AddText("Arguments", "启动参数", original.Arguments);
        AddPath("WorkingDirectory", "工作目录", original.WorkingDirectory, "选择目录…", () => this.folderPicker.SelectFolder(inputs["WorkingDirectory"].Text));
        AddCheck("RdpFullScreen", "RDP 全屏", original.RdpFullScreen); AddCheck("Clipboard", "RDP 剪贴板共享", original.Clipboard);
        AddWide(expandAdvanced); AddWide(advanced);
        expandAdvanced.CheckedChanged += (_, _) => { advanced.Visible = expandAdvanced.Checked; expandAdvanced.Text = expandAdvanced.Checked ? "收起高级设置" : "高级设置（可选）"; };
        advanced.Visible = false;
        var note = new Label { Name = "EditorNote", AutoSize = true, Text = "仅连接不写入会话或密码；仅保存不建立连接。\n终端使用 UTF-8；SSH 私钥留空时使用密码，填写时密码框用作私钥口令。\nRDP 在 Windows 远程桌面窗口打开，密码不会自动注入系统凭据。" };
        AddWide(note);
        var buttons = new FlowLayoutPanel { Name = "EditorActions", Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTheme.Inset), WrapContents = true };
        var cancel = new UiButton { Name = "CancelEditor", Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new UiButton { Name = "SaveOnly", Text = "仅保存", AutoSize = true };
        connectOnly.TabIndex = 0; save.TabIndex = 1; connect.TabIndex = 2; cancel.TabIndex = 3;
        connect.Click += (_, _) => Finish(true, true); save.Click += (_, _) => Finish(false, true); connectOnly.Click += (_, _) => Finish(true, false);
        buttons.Controls.AddRange([cancel, connect, save, connectOnly]); AcceptButton = connect; CancelButton = cancel;
        var footer = new Panel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        footer.Controls.Add(feedback); footer.Controls.Add(buttons);
        Controls.Add(scroll); Controls.Add(footer);
        foreach (var label in new[] { availability, feedback }) UiTheme.Role(label, UiRole.Error);
        UiTheme.Role(serialNotice, UiRole.Muted); UiTheme.Role(note, UiRole.Muted);
        showPassword.CheckedChanged += (_, _) => password.UseSystemPasswordChar = !showPassword.Checked;
        Deactivate += (_, _) => showPassword.Checked = false;
        lastProtocol = SelectedProtocol; portsByProtocol[lastProtocol] = (int)((NumericUpDown)inputs["Port"]).Value;
        protocol.SelectedIndexChanged += (_, _) =>
        {
            portsByProtocol[lastProtocol] = (int)((NumericUpDown)inputs["Port"]).Value;
            lastProtocol = SelectedProtocol;
            ((NumericUpDown)inputs["Port"]).Value = portsByProtocol.GetValueOrDefault(lastProtocol, Session.DefaultPort(lastProtocol));
            UpdateFields(); if (validationStarted) ValidateDraft(false);
        };
        Activated += (_, _) => UpdateAvailability();
        UpdateFields();
    }
    static TableLayoutPanel CreateTable(string name)
    {
        var table = new TableLayoutPanel { Name = name, Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, GrowStyle = TableLayoutPanelGrowStyle.AddRows, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return table;
    }
    Protocol SelectedProtocol => Enum.Parse<Protocol>(protocol.Text);
    void AddHeading(string text, string name)
    {
        var label = new Label { Name = name, Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        UiTheme.Role(label, UiRole.Heading); AddWide(label);
    }
    void AddWide(Control control)
    {
        int row = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Top; fields.Controls.Add(control, 0, row); fields.SetColumnSpan(control, 2);
        if (control is Label) fields.SizeChanged += (_, _) => control.MaximumSize = new Size(Math.Max(1, fields.ClientSize.Width - fields.Padding.Horizontal), 0);
    }
    void Add(string key, string label, Control control, Control? display = null)
    {
        var table = advancedKeys.Contains(key) ? advanced : fields;
        int index = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 0, 0), Margin = new Padding(0, 0, 8, 8) };
        control.Name = key; control.AccessibleName = label.Length > 0 ? label : control.Text;
        var stack = new TableLayoutPanel { Name = "Row" + key, Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 3, 0, 8) };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var shown = display ?? control; shown.Dock = DockStyle.Top; shown.Margin = Padding.Empty;
        var error = new Label { Name = "Error" + key, AutoSize = true, Dock = DockStyle.Top, Visible = false, Margin = new Padding(0, 3, 0, 0) };
        UiTheme.Role(error, UiRole.Error);
        stack.Controls.Add(shown, 0, 0); stack.Controls.Add(error, 0, 1);
        table.Controls.Add(title, 0, index); table.Controls.Add(stack, 1, index);
        inputs[key] = control; rows[key] = (title, stack); errors[key] = error;
        control.TextChanged += (_, _) => { if (validationStarted) ValidateDraft(false); };
        if (control is NumericUpDown number) number.ValueChanged += (_, _) => { if (validationStarted) ValidateDraft(false); };
        stack.SizeChanged += (_, _) =>
        {
            var limit = new Size(Math.Max(1, stack.ClientSize.Width), 0); error.MaximumSize = limit;
            if (shown is Label or CheckBox) shown.MaximumSize = limit;
        };
    }
    void AddText(string key, string title, string value) => Add(key, title, new TextBox { Text = value });
    void AddNumber(string key, string title, int value, int min, int max) => Add(key, title, new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max) });
    void AddCheck(string key, string title, bool value) => Add(key, "", new CheckBox { Text = title, Checked = value, AutoSize = true });
    void AddChoice(string key, string title, string[] values, string value) { var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; box.Items.AddRange(values); box.SelectedItem = value; if (box.SelectedIndex < 0) box.SelectedIndex = 0; Add(key, title, box); }
    void AddPath(string key, string title, string value, string buttonText, Func<string?> pick)
    {
        var text = new TextBox { Text = value, Dock = DockStyle.Fill };
        var button = new UiButton { Name = "Browse" + key, Text = buttonText, AutoSize = true, AccessibleName = buttonText };
        var panel = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Dock = DockStyle.Top, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(text, 0, 0); panel.Controls.Add(button, 1, 0);
        button.Click += (_, _) =>
        {
            try { var path = pick(); if (path != null) text.Text = path; text.Focus(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception or ArgumentException)
            { feedback.Text = "无法打开路径选择器；可直接输入路径后重试。"; }
        };
        Add(key, title, text, panel);
    }
    void UpdateFields()
    {
        var p = SelectedProtocol;
        foreach (var (key, pair) in rows)
        {
            bool visible = key switch {
                "Host" or "Port" => p is not (Protocol.Serial or Protocol.Shell),
                "Username" or "Password" or "ShowPassword" or "SavePassword" => p is Protocol.SSH or Protocol.SFTP or Protocol.FTP or Protocol.RDP,
                "PrivateKey" => p is Protocol.SSH or Protocol.SFTP,
                "RemotePath" => p is Protocol.FTP or Protocol.SFTP,
                "UseTls" => p == Protocol.FTP,
                "SerialPort" or "SerialNotice" or "BaudRate" or "DataBits" or "Parity" or "StopBits" or "Handshake" => p == Protocol.Serial,
                "Executable" or "Arguments" or "WorkingDirectory" => p == Protocol.Shell,
                "RdpFullScreen" or "Clipboard" => p == Protocol.RDP,
                _ => true };
            pair.label.Visible = pair.field.Visible = visible;
        }
        if (p is not (Protocol.SSH or Protocol.SFTP or Protocol.FTP or Protocol.RDP)) showPassword.Checked = false;
        rows["Username"].label.Text = inputs["Username"].AccessibleName = p is Protocol.SSH or Protocol.SFTP ? "用户名（必填）" : "用户名（可选）";
        ((TextBox)inputs["Name"]).PlaceholderText = p switch
        {
            Protocol.Serial => "留空时使用串口名称",
            Protocol.Shell => "留空时使用程序名",
            _ => "留空时使用主机 / IP"
        };
        UpdateAvailability();
    }
    void UpdateAvailability()
    {
        var state = platform.GetAvailability(SelectedProtocol);
        connect.Enabled = connectOnly.Enabled = state.IsAvailable;
        availability.Text = state.IsAvailable ? "" : PlatformCapabilityPresenter.ForProtocol(SelectedProtocol, state) + " 仍可仅保存会话。";
        rows["PlatformAvailability"].label.Visible = rows["PlatformAvailability"].field.Visible = !state.IsAvailable;
    }
    Session Draft()
    {
        string S(string key) => inputs[key].Text.Trim(); int N(string key) => (int)((NumericUpDown)inputs[key]).Value; bool B(string key) => ((CheckBox)inputs[key]).Checked;
        return SessionDraftDefaults.ApplyName(original with { Name = S("Name"), Group = S("Group"), Protocol = SelectedProtocol, Host = S("Host"), Port = N("Port"), Username = S("Username"), PrivateKey = S("PrivateKey"), RemotePath = S("RemotePath"), UseTls = B("UseTls"), SerialPort = S("SerialPort"), BaudRate = N("BaudRate"), DataBits = N("DataBits"), Parity = S("Parity"), StopBits = S("StopBits"), Handshake = S("Handshake"), Executable = S("Executable"), Arguments = inputs["Arguments"].Text, WorkingDirectory = S("WorkingDirectory"), RdpFullScreen = B("RdpFullScreen"), Clipboard = B("Clipboard"), SavePassword = savePassword.Checked && SelectedProtocol is Protocol.SSH or Protocol.SFTP or Protocol.FTP or Protocol.RDP });
    }
    IReadOnlyList<SessionValidationError> ValidateDraft(bool focus)
    {
        var draft = Draft();
        var failures = draft.GetValidationErrors(requireAuthentication: true).Where(error => error.Field != "Name").ToList();
        try { GroupPath.Validate(draft.Group); } catch (ArgumentException error) { failures.Add(new("Group", error.Message)); }
        foreach (var (key, label) in errors)
        {
            label.Text = string.Join(" ", failures.Where(e => e.Field == key).Select(e => e.Message)); label.Visible = label.Text.Length > 0;
            inputs[key].AccessibleDescription = label.Text;
        }
        feedback.Text = failures.Count == 0 ? "" : "请修正标出的参数；未保存或连接。";
        if (focus && failures.FirstOrDefault() is { } first && inputs.TryGetValue(first.Field, out var input))
        {
            if (advancedKeys.Contains(first.Field)) expandAdvanced.Checked = true;
            scroll.ScrollControlIntoView(input); input.Focus();
        }
        return failures;
    }
    void Finish(bool connectNow, bool save)
    {
        UpdateAvailability();
        if (connectNow && !connect.Enabled) return;
        validationStarted = true;
        if (ValidateDraft(true).Count > 0) return;
        Result = Draft(); ConnectNow = connectNow; Persist = save; DialogResult = DialogResult.OK; Close();
    }
}

public static class TextPrompt
{
    public static string? Ask(IWin32Window owner, string title, string label, string initial = "", bool secret = false)
    {
        using var form = new Form { Text = title, Size = new Size(460, 180), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var text = new TextBox { Text = initial, UseSystemPasswordChar = secret, Left = 20, Top = 50, Width = 400 };
        form.Controls.Add(new Label { Text = label, Left = 20, Top = 18, AutoSize = true }); form.Controls.Add(text);
        var ok = new UiButton { Text = "确定", DialogResult = DialogResult.OK, Left = 250, Top = 88, Kind = UiButtonKind.Primary }; var cancel = new UiButton { Text = "取消", DialogResult = DialogResult.Cancel, Left = 335, Top = 88 };
        form.Controls.AddRange([ok, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        UiTheme.AttachDialog(form);
        return form.ShowDialog(owner) == DialogResult.OK ? text.Text : null;
    }

    public static string? AskWithSkip(IWin32Window owner, string title, string label, string initial = "", bool secret = false)
    {
        using var form = new Form { Text = title, Size = new Size(460, 180), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var text = new TextBox { Text = initial, UseSystemPasswordChar = secret, Left = 20, Top = 50, Width = 400 };
        form.Controls.Add(new Label { Text = label, Left = 20, Top = 18, AutoSize = true }); form.Controls.Add(text);
        var ok = new UiButton { Text = "确定", DialogResult = DialogResult.OK, Left = 175, Top = 88, Kind = UiButtonKind.Primary };
        var skip = new UiButton { Text = "跳过", DialogResult = DialogResult.Ignore, Left = 260, Top = 88 };
        var cancel = new UiButton { Text = "取消", DialogResult = DialogResult.Cancel, Left = 345, Top = 88 };
        form.Controls.AddRange([ok, skip, cancel]); form.AcceptButton = ok; form.CancelButton = cancel;
        UiTheme.AttachDialog(form);
        return form.ShowDialog(owner) switch { DialogResult.OK => text.Text, DialogResult.Ignore => "", _ => null };
    }
}

