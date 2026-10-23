using System.Collections.Concurrent;
using System.Text;

namespace PortHarbor;

public sealed class TerminalView : Control
{
    public TerminalScreen Screen { get; } = new();
    public event Action<byte[]>? Input;
    public event Action<int, int>? TerminalResized;
    readonly Font terminalFont;
    readonly ConcurrentQueue<string> output = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    int cw = 9, ch = 18, scroll;
    readonly VScrollBar scrollbar = new() { Dock = DockStyle.Right, Width = 18 };
    public event Action<Font>? FontChangedByUser;
    public bool CopyOnSelection { get; private set; }
    public bool PasteOnRightClick { get; private set; }
    Point? selectionStart, selectionEnd;
    public TerminalView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        BackColor = TerminalScreen.DefaultBackground; ForeColor = TerminalScreen.DefaultForeground;
        Font = terminalFont = CreateTerminalFont(); TabStop = true;
        Screen.Reply += Send;
        timer.Tick += (_, _) => { var b = new StringBuilder(); while (b.Length < 200000 && output.TryDequeue(out var text)) b.Append(text); if (b.Length > 0) { Screen.Feed(b.ToString()); SyncScrollbar(); Invalidate(); } };
        timer.Start();
        scrollbar.Scroll += (_, _) => { scroll = ScrollMaximum - scrollbar.Value; Invalidate(); };
        Controls.Add(scrollbar);
        var menu = new ContextMenuStrip();
        menu.Items.Add("复制选择 / 全部  Ctrl+Shift+C", null, (_, _) => Copy());
        menu.Items.Add("粘贴  Ctrl+Shift+V", null, (_, _) => Paste());
        var copyOnSelection = new ToolStripMenuItem("左键选取后自动复制") { CheckOnClick = true };
        copyOnSelection.CheckedChanged += (_, _) => CopyOnSelection = copyOnSelection.Checked;
        menu.Items.Add(copyOnSelection);
        var pasteOnRightClick = new ToolStripMenuItem("右键直接粘贴") { CheckOnClick = true };
        pasteOnRightClick.CheckedChanged += (_, _) => PasteOnRightClick = pasteOnRightClick.Checked;
        menu.Items.Add(pasteOnRightClick);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("终端字体…", null, (_, _) => ConfigureAppearance());
        menu.Items.Add("终端配色…", null, (_, _) => ConfigureColors());
        ContextMenuStrip = menu;
    }
    internal static Font CreateTerminalFont()
    {
        using var installed = new System.Drawing.Text.InstalledFontCollection();
        foreach (string name in new[] { "Consolas", "Cascadia Mono", "Lucida Console", "Courier New" })
            if (installed.Families.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return new Font(name, 11);
        return new Font(FontFamily.GenericMonospace, 11);
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); RecalculateCells(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); RecalculateCells(); }
    void RecalculateCells()
    {
        var size = TextRenderer.MeasureText(new string('M', 80), Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        cw = Math.Max(1, (int)Math.Round(size.Width / 80.0)); ch = Math.Max(1, size.Height + (int)Math.Ceiling(DeviceDpi / 96.0));
        if (Screen == null) return;
        int cols = Math.Max(10, (Width - 12) / cw), rows = Math.Max(3, (Height - 8) / ch);
        Screen.Resize(cols, rows); SyncScrollbar(); TerminalResized?.Invoke(cols, rows); Invalidate();
    }
    public void Append(string text) => output.Enqueue(text);
    void SyncScrollbar()
    {
        scrollbar.Maximum = Math.Max(0, Screen.History.Count + Screen.Rows - 1);
        scrollbar.LargeChange = Math.Max(1, Screen.Rows);
        scrollbar.SmallChange = 3;
        scrollbar.Value = Math.Clamp(ScrollMaximum - scroll, scrollbar.Minimum, ScrollMaximum);
    }
    int ScrollMaximum => Math.Max(0, scrollbar.Maximum - scrollbar.LargeChange + 1);
    void ConfigureAppearance()
    {
        using var dialog = new FontDialog { Font = Font, ShowColor = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Font = new Font(dialog.Font.FontFamily, dialog.Font.Size, dialog.Font.Style, GraphicsUnit.Point);
        FontChangedByUser?.Invoke(Font);
    }
    void ConfigureColors()
    {
        using var foreground = new ColorDialog { Color = Screen.ForegroundColor, FullOpen = true };
        if (foreground.ShowDialog(this) != DialogResult.OK) return;
        using var background = new ColorDialog { Color = Screen.BackgroundColor, FullOpen = true };
        if (background.ShowDialog(this) != DialogResult.OK) return;
        Screen.SetDefaultColors(foreground.Color, background.Color);
        ForeColor = foreground.Color; BackColor = background.Color; Invalidate();
    }

    void Send(string value) { scroll = 0; selectionStart = selectionEnd = null; Input?.Invoke(Encoding.UTF8.GetBytes(value)); Invalidate(); }
    public void Copy()
    {
        string value;
        if (selectionStart is Point a && selectionEnd is Point b)
        {
            var rows = Screen.History.Concat(Screen.Lines).ToArray();
            int first = Math.Min(a.Y * Screen.Columns + a.X, b.Y * Screen.Columns + b.X), last = Math.Max(a.Y * Screen.Columns + a.X, b.Y * Screen.Columns + b.X);
            var text = new StringBuilder();
            for (int y = first / Screen.Columns; y <= last / Screen.Columns && y < rows.Length; y++)
            {
                int x0 = y == first / Screen.Columns ? first % Screen.Columns : 0, x1 = y == last / Screen.Columns ? last % Screen.Columns : Screen.Columns - 1;
                text.Append(string.Concat(rows[y].Skip(x0).Take(x1 - x0 + 1).Select(c => c.Text)).TrimEnd());
                if (y < last / Screen.Columns) text.AppendLine();
            }
            value = text.ToString();
        }
        else value = Screen.Text;
        try { if (value.Length > 0) Clipboard.SetText(value); } catch (System.Runtime.InteropServices.ExternalException) { }
    }
    public void Paste()
    {
        if (!Clipboard.ContainsText()) return;
        var text = Clipboard.GetText().Replace("\r\n", "\n");
        if (text.Contains('\n') && MessageBox.Show(this, "粘贴内容包含多行，可能立即执行命令。继续？", "粘贴", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Send(Screen.BracketedPaste ? "\x1b[200~" + text + "\x1b[201~" : text.Replace("\n", "\r"));
    }
    protected override bool IsInputKey(Keys keyData) => true;
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.Shift && e.KeyCode == Keys.C) { Copy(); e.SuppressKeyPress = true; return; }
        if (e.Control && e.Shift && e.KeyCode == Keys.V) { Paste(); e.SuppressKeyPress = true; return; }
        string prefix = Screen.ApplicationCursor ? "\x1bO" : "\x1b[";
        string? value = e.KeyCode switch { Keys.Up => prefix + "A", Keys.Down => prefix + "B", Keys.Right => prefix + "C", Keys.Left => prefix + "D", Keys.Home => "\x1b[H", Keys.End => "\x1b[F", Keys.Delete => "\x1b[3~", Keys.Insert => "\x1b[2~", Keys.PageUp => "\x1b[5~", Keys.PageDown => "\x1b[6~", Keys.F1 => "\x1bOP", Keys.F2 => "\x1bOQ", Keys.F3 => "\x1bOR", Keys.F4 => "\x1bOS", Keys.F5 => "\x1b[15~", Keys.F6 => "\x1b[17~", Keys.F7 => "\x1b[18~", Keys.F8 => "\x1b[19~", Keys.F9 => "\x1b[20~", Keys.F10 => "\x1b[21~", Keys.F11 => "\x1b[23~", Keys.F12 => "\x1b[24~", _ => null };
        if (e.Control && !e.Alt && e.KeyCode is >= Keys.A and <= Keys.Z) value = ((char)(e.KeyCode - Keys.A + 1)).ToString();
        if (value != null) { Send(value); e.SuppressKeyPress = true; }
    }
    protected override void OnKeyPress(KeyPressEventArgs e) { Send(e.KeyChar == '\b' ? "\x7f" : e.KeyChar.ToString()); e.Handled = true; base.OnKeyPress(e); }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateCells();
    }
    protected override void OnMouseWheel(MouseEventArgs e) { scroll = Math.Clamp(scroll + (e.Delta > 0 ? 3 : -3), 0, Screen.History.Count); SyncScrollbar(); Invalidate(); base.OnMouseWheel(e); }
    Point CellAt(Point p) => new(Math.Clamp((p.X - 6) / cw, 0, Screen.Columns - 1), Math.Max(0, Screen.History.Count - scroll + Math.Clamp((p.Y - 4) / ch, 0, Screen.Rows - 1)));
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Right && PasteOnRightClick) { Paste(); return; } if (e.Button == MouseButtons.Left) { selectionStart = selectionEnd = CellAt(e.Location); Capture = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (Capture && selectionStart != null) { selectionEnd = CellAt(e.Location); Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left && CopyOnSelection && selectionStart != null && selectionEnd != null) Copy(); Capture = false; base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var rows = Screen.History.Concat(Screen.Lines).ToArray();
        int first = Screen.History.Count - Math.Min(scroll, Screen.History.Count);
        int a = selectionStart is Point p ? p.Y * Screen.Columns + p.X : -1, b = selectionEnd is Point q ? q.Y * Screen.Columns + q.X : -1;
        for (int y = 0; y < Screen.Rows && first + y < rows.Length; y++)
        {
            var row = rows[first + y];
            for (int x = 0; x < Math.Min(row.Length, Screen.Columns); x++)
            {
                var c = row[x]; var rect = new Rectangle(6 + x * cw, 4 + y * ch, cw, ch);
                int index = (first + y) * Screen.Columns + x;
                Color background = a >= 0 && index >= Math.Min(a, b) && index <= Math.Max(a, b) ? Color.FromArgb(62, 82, 118) : c.Background;
                using var brush = new SolidBrush(background); e.Graphics.FillRectangle(brush, rect);
            }
            // Paint all cell backgrounds before glyphs so wide CJK glyphs survive.
            for (int x = 0; x < Math.Min(row.Length, Screen.Columns); x++)
            {
                var c = row[x];
                if (c.Text.Length > 0 && c.Text != " ") TextRenderer.DrawText(e.Graphics, c.Text, Font, new Point(6 + x * cw, 4 + y * ch), c.Foreground, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }
        if (Focused && scroll == 0 && Screen.CursorVisible) { using var pen = new Pen(Color.LightSkyBlue, 2); e.Graphics.DrawLine(pen, 6 + Screen.X * cw, 4 + (Screen.Y + 1) * ch - 2, 6 + (Screen.X + 1) * cw - 1, 4 + (Screen.Y + 1) * ch - 2); }
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); terminalFont.Dispose(); scrollbar.Dispose(); ContextMenuStrip?.Dispose(); } base.Dispose(disposing); }
}



