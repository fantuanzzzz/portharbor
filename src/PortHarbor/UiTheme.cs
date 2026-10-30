using System.Runtime.CompilerServices;

namespace PortHarbor;

internal enum UiRole { Normal, Muted, Heading, Success, Warning, Error, Sidebar }
internal enum UiButtonKind { Secondary, Primary, Danger, Card }

internal sealed record UiPalette(Color Page, Color Surface, Color Subtle, Color Text, Color Muted, Color Accent,
    Color OnAccent, Color Selected, Color Border, Color Success, Color Warning, Color Error, Color Disabled, Color DisabledText,
    bool HighContrast = false)
{
    static Color C(int rgb) => Color.FromArgb(unchecked((int)0xFF000000) | rgb);
    internal static UiPalette Resolve(InterfaceTheme theme, bool highContrast)
    {
        if (highContrast) return new(SystemColors.Control, SystemColors.Window, SystemColors.Control, SystemColors.WindowText,
            SystemColors.WindowText, SystemColors.Highlight, SystemColors.HighlightText, SystemColors.Highlight,
            SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText,
            SystemColors.Control, SystemColors.GrayText, true);
        return theme == InterfaceTheme.Dark
            ? new(C(0x151C29), C(0x202A3B), C(0x192231), C(0xE6EDF7), C(0xADBBD0), C(0x245FC7), Color.White,
                C(0x304562), C(0x65758B), C(0x80DAB0), C(0xF0CE79), C(0xFFADB5), C(0x263143), C(0xA0AEC2))
            : new(C(0xF4F6FA), Color.White, C(0xEDF1F7), C(0x202C40), C(0x53647A), C(0x245FC7), Color.White,
                C(0xE2ECFC), C(0xB9C5D6), C(0x17653E), C(0x805600), C(0xB32635), C(0xE5EAF1), C(0x59697F));
    }
}

internal static class UiTheme
{
    internal const int SmallGap = 4, Gap = 8, Inset = 12, Section = 16, PageInset = 24, ButtonHeight = 32;
    internal static readonly Font BodyFont = new("Microsoft YaHei UI", 9);
    static readonly ConditionalWeakTable<Control, Metadata> Styles = new();
    sealed class Metadata
    {
        internal InterfaceTheme? Theme;
        internal UiRole Role;
        internal bool Attached;
    }
    internal static UiPalette Palette(Control control) => UiPalette.Resolve(Choice(control), SystemInformation.HighContrast);
    internal static InterfaceTheme Choice(Control control)
    {
        for (Control? current = control; current != null; current = current.Parent)
            if (Styles.TryGetValue(current, out var style) && style.Theme is { } theme) return theme;
        if (control.FindForm()?.Owner is { } owner) return Choice(owner);
        return InterfaceTheme.Light;
    }
    internal static void Role(Control control, UiRole role) { Styles.GetOrCreateValue(control).Role = role; Refresh(control); }
    internal static void AttachDialog(Form form)
    {
        form.Font = BodyFont; form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Load += (_, _) => Apply(form, form.Owner == null ? InterfaceTheme.Light : Choice(form.Owner));
    }
    internal static void Apply(Control root, InterfaceTheme theme)
    {
        Styles.GetOrCreateValue(root).Theme = theme;
        PaintTree(root, UiPalette.Resolve(theme, SystemInformation.HighContrast));
        root.Invalidate(true);
    }
    internal static void Refresh(Control control) => PaintTree(control, Palette(control));
    static void PaintTree(Control control, UiPalette p)
    {
        if (control is TerminalView) return;
        var metadata = Styles.GetOrCreateValue(control);
        if (!metadata.Attached)
        {
            metadata.Attached = true;
            control.ControlAdded += (_, e) => { if (e.Control != null) Refresh(e.Control); };
            control.SystemColorsChanged += (_, _) => { if (!control.IsDisposed) Refresh(control); };
        }
        control.ForeColor = metadata.Role switch
        { UiRole.Muted => p.Muted, UiRole.Success => p.Success, UiRole.Warning => p.Warning, UiRole.Error => p.Error, _ => p.Text };
        control.BackColor = control switch
        {
            TreeView => p.Subtle,
            TextBoxBase or ListView or ComboBox or NumericUpDown or TabPage => p.Surface,
            Form or UserControl => p.Page,
            SplitContainer => p.Border,
            _ => metadata.Role == UiRole.Sidebar ? p.Subtle : control.Parent?.BackColor ?? p.Page
        };
        switch (control)
        {
            case UiButton button: button.ApplyPalette(p); break;
            case ToolStrip strip: StyleStrip(strip, p); break;
            case TreeView tree:
                tree.LineColor = p.Border;
                tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
                if (!treeStyles.TryGetValue(tree, out _)) { treeStyles.Add(tree, new()); tree.DrawNode += DrawTreeNode; }
                break;
            case ListView list:
                list.BorderStyle = BorderStyle.FixedSingle;
                if (!listStyles.TryGetValue(list, out _))
                {
                    listStyles.Add(list, new()); list.OwnerDraw = true;
                    list.DrawColumnHeader += DrawColumn;
                    list.DrawItem += (_, e) => { if (list.View != View.Details) e.DrawDefault = true; };
                    list.DrawSubItem += DrawSubItem;
                }
                break;
        }
        if (control.ContextMenuStrip is { } context) StyleStrip(context, p);
        foreach (Control child in control.Controls) PaintTree(child, p);
        control.Invalidate();
    }
    static readonly ConditionalWeakTable<TreeView, object> treeStyles = new();
    static readonly ConditionalWeakTable<ListView, object> listStyles = new();
    static void DrawTreeNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (sender is not TreeView tree || e.Node == null) return;
        var p = Palette(tree);
        if (p.HighContrast) { e.DrawDefault = true; return; }
        bool selected = e.Node == tree.SelectedNode;
        var rect = new Rectangle(e.Bounds.Left, e.Bounds.Top, Math.Max(0, tree.ClientSize.Width - e.Bounds.Left), e.Bounds.Height);
        using var fill = new SolidBrush(selected ? p.Selected : tree.BackColor); e.Graphics.FillRectangle(fill, rect);
        TextRenderer.DrawText(e.Graphics, e.Node.Text, tree.Font, rect, p.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (selected && tree.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, rect, p.Text, p.Selected);
    }
    static void DrawColumn(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        if (sender is not ListView list) return;
        var p = Palette(list); if (p.HighContrast) { e.DrawDefault = true; return; }
        using var fill = new SolidBrush(p.Subtle); e.Graphics.FillRectangle(fill, e.Bounds);
        using var edge = new Pen(p.Border); e.Graphics.DrawLine(edge, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text, list.Font, Rectangle.Inflate(e.Bounds, -8, 0), p.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    static void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (sender is not ListView list || e.Item == null || e.SubItem == null) return;
        var p = Palette(list); if (p.HighContrast) { e.DrawDefault = true; return; }
        var back = e.Item.Selected ? p.Selected : p.Surface;
        using var fill = new SolidBrush(back); e.Graphics.FillRectangle(fill, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, Rectangle.Inflate(e.Bounds, -8, 0), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (e.Item.Selected && list.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, p.Text, back);
    }
    static void StyleStrip(ToolStrip strip, UiPalette p)
    {
        strip.BackColor = p.Surface; strip.ForeColor = p.Text;
        strip.Renderer = p.HighContrast ? new ToolStripSystemRenderer() : new UiStripRenderer(p);
        void Items(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = p.Text; item.BackColor = p.Surface;
                if (item is ToolStripDropDownItem drop) { drop.DropDown.BackColor = p.Surface; drop.DropDown.Renderer = strip.Renderer; Items(drop.DropDownItems); }
            }
        }
        Items(strip.Items);
    }
    sealed class UiStripRenderer(UiPalette p) : ToolStripProfessionalRenderer(new UiColorTable(p))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        { e.TextColor = e.Item.Enabled ? e.Item.ForeColor : p.DisabledText; base.OnRenderItemText(e); }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        { e.ArrowColor = e.Item?.Enabled == false ? p.DisabledText : p.Text; base.OnRenderArrow(e); }
    }
    sealed class UiColorTable(UiPalette p) : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => p.Surface;
        public override Color ToolStripGradientMiddle => p.Surface;
        public override Color ToolStripGradientEnd => p.Surface;
        public override Color MenuStripGradientBegin => p.Surface;
        public override Color MenuStripGradientEnd => p.Surface;
        public override Color ToolStripDropDownBackground => p.Surface;
        public override Color ImageMarginGradientBegin => p.Surface;
        public override Color ImageMarginGradientMiddle => p.Surface;
        public override Color ImageMarginGradientEnd => p.Surface;
        public override Color StatusStripGradientBegin => p.Subtle;
        public override Color StatusStripGradientEnd => p.Subtle;
        public override Color MenuItemSelected => p.Selected;
        public override Color MenuItemSelectedGradientBegin => p.Selected;
        public override Color MenuItemSelectedGradientEnd => p.Selected;
        public override Color MenuItemPressedGradientBegin => p.Selected;
        public override Color MenuItemPressedGradientMiddle => p.Selected;
        public override Color MenuItemPressedGradientEnd => p.Selected;
        public override Color ButtonSelectedGradientBegin => p.Selected;
        public override Color ButtonSelectedGradientMiddle => p.Selected;
        public override Color ButtonSelectedGradientEnd => p.Selected;
        public override Color ButtonPressedGradientBegin => p.Selected;
        public override Color ButtonPressedGradientMiddle => p.Selected;
        public override Color ButtonPressedGradientEnd => p.Selected;
        public override Color ButtonCheckedGradientBegin => p.Selected;
        public override Color ButtonCheckedGradientMiddle => p.Selected;
        public override Color ButtonCheckedGradientEnd => p.Selected;
        public override Color MenuItemBorder => p.Border;
        public override Color MenuBorder => p.Border;
        public override Color ToolStripBorder => p.Border;
        public override Color SeparatorDark => p.Border;
        public override Color SeparatorLight => p.Surface;
    }
}

internal sealed class UiButton : Button
{
    internal UiButtonKind Kind { get; set; }
    bool hover, pressed;
    UiPalette palette = UiPalette.Resolve(InterfaceTheme.Light, SystemInformation.HighContrast);
    internal UiButton()
    {
        FlatStyle = FlatStyle.Flat; MinimumSize = new Size(76, UiTheme.ButtonHeight);
        Padding = new Padding(UiTheme.Inset, UiTheme.SmallGap, UiTheme.Inset, UiTheme.SmallGap);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    internal void ApplyPalette(UiPalette value)
    {
        palette = value;
        if (value.HighContrast)
        {
            BackColor = SystemColors.Control; ForeColor = SystemColors.ControlText;
            FlatStyle = FlatStyle.Standard; UseVisualStyleBackColor = true; Invalidate(); return;
        }
        BackColor = Kind == UiButtonKind.Primary ? value.Accent : value.Surface;
        ForeColor = Kind == UiButtonKind.Primary ? value.OnAccent : Kind == UiButtonKind.Danger ? value.Error : value.Text;
        FlatStyle = value.HighContrast ? FlatStyle.Standard : FlatStyle.Flat;
        UseVisualStyleBackColor = value.HighContrast;
        Invalidate();
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (palette.HighContrast) { base.OnPaint(e); return; }
        bool primary = Kind == UiButtonKind.Primary;
        Color back = !Enabled ? palette.Disabled : primary ? pressed ? Color.FromArgb(21, 62, 135)
            : hover ? Color.FromArgb(28, 79, 167) : palette.Accent : hover || pressed ? palette.Selected : palette.Surface;
        Color ink = !Enabled ? palette.DisabledText : primary ? palette.OnAccent : Kind == UiButtonKind.Danger ? palette.Error : palette.Text;
        using var fill = new SolidBrush(back); e.Graphics.FillRectangle(fill, ClientRectangle);
        int edgeWidth = Focused || pressed ? 2 : 1;
        using var edge = new Pen(Focused ? palette.Accent : palette.Border, edgeWidth);
        var border = ClientRectangle; border.Width--; border.Height--; e.Graphics.DrawRectangle(edge, border);
        var text = Rectangle.Inflate(ClientRectangle, -Padding.Horizontal / 2, -Padding.Vertical / 2);
        if (pressed) text.Offset(1, 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), ink, back);
    }
}
