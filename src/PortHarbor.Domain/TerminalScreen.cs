using System.Drawing;
using System.Text;

namespace PortHarbor;

public record struct Cell(string Text, Color Foreground, Color Background);
public sealed class TerminalScreen
{
    public static readonly Color DefaultForeground = Color.FromArgb(220, 227, 238), DefaultBackground = Color.FromArgb(17, 24, 39);
    static readonly Color[] Palette = [Color.Black, Color.IndianRed, Color.MediumSeaGreen, Color.Goldenrod, Color.CornflowerBlue, Color.MediumOrchid, Color.Turquoise, Color.LightGray,
        Color.Gray, Color.Salmon, Color.LightGreen, Color.Khaki, Color.LightSkyBlue, Color.Plum, Color.LightCyan, Color.White];
    public Cell[][] Lines { get; private set; }
    public List<Cell[]> History { get; } = [];
    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    public bool ApplicationCursor { get; private set; }
    public bool BracketedPaste { get; private set; }
    public event Action<string>? Reply;
    Color fg = DefaultForeground, bg = DefaultBackground;
    Color defaultFg = DefaultForeground, defaultBg = DefaultBackground;
    public Color ForegroundColor => fg;
    public Color BackgroundColor => bg;
    public void SetDefaultColors(Color foreground, Color background)
    {
        var oldFg = defaultFg; var oldBg = defaultBg;
        foreach (var row in History.Concat(Lines).Concat(primary ?? []))
            for (int x = 0; x < row.Length; x++)
                row[x] = row[x] with { Foreground = row[x].Foreground == oldFg ? foreground : row[x].Foreground,
                    Background = row[x].Background == oldBg ? background : row[x].Background };
        defaultFg = fg = foreground; defaultBg = bg = background;
    }
    int state, savedX, savedY, top, bottom;
    bool wrapPending, alternate;
    Cell[][]? primary;
    int primaryX, primaryY;
    readonly StringBuilder sequence = new();
    char? highSurrogate;
    public TerminalScreen(int columns = 100, int rows = 30)
    {
        Columns = columns; Rows = rows; bottom = rows - 1;
        Lines = Enumerable.Range(0, rows).Select(_ => Blank()).ToArray();
    }
    Cell Empty => new(" ", fg, bg);
    Cell[] Blank() => Enumerable.Repeat(Empty, Columns).ToArray();
    public void Resize(int columns, int rows)
    {
        columns = Math.Clamp(columns, 10, 400); rows = Math.Clamp(rows, 2, 150);
        if (Columns == columns && Rows == rows) return;
        int oldColumns = Columns; Columns = columns;
        Cell[][] ConvertLines(Cell[][] source) => Enumerable.Range(0, rows).Select(i => { var row = Blank(); if (i < source.Length) Array.Copy(source[i], row, Math.Min(oldColumns, columns)); return row; }).ToArray();
        Lines = ConvertLines(Lines);
        if (primary != null) primary = ConvertLines(primary);
        Rows = rows; X = Math.Min(X, columns - 1); Y = Math.Min(Y, rows - 1); top = 0; bottom = rows - 1; wrapPending = false;
    }
    public void Feed(string value)
    {
        foreach (char c in value)
        {
            if (state == 3) { if (c == '\a') state = 0; else if (c == '\x1b') state = 4; continue; }
            if (state == 4) { state = c == '\\' ? 0 : 3; continue; }
            if (c == '\x1b') { state = 1; sequence.Clear(); continue; }
            if (state == 1)
            {
                state = 0;
                switch (c)
                {
                    case '[': state = 2; break;
                    case ']': case 'P': case '^': case '_': state = 3; break;
                    case '7': savedX = X; savedY = Y; break;
                    case '8': X = Math.Clamp(savedX, 0, Columns - 1); Y = Math.Clamp(savedY, 0, Rows - 1); break;
                    case 'D': LineFeed(); break;
                    case 'M': if (Y == top) ScrollDown(); else Y = Math.Max(0, Y - 1); break;
                    case 'E': X = 0; LineFeed(); break;
                    case 'c': fg = defaultFg; bg = defaultBg; X = Y = 0; Lines = Enumerable.Range(0, Rows).Select(_ => Blank()).ToArray(); break;
                    case '(': case ')': state = 5; break;
                }
                continue;
            }
            if (state == 5) { state = 0; continue; }
            if (state == 2)
            {
                if (c is >= '@' and <= '~') { Execute(c, sequence.ToString()); state = 0; }
                else if (sequence.Length < 128) sequence.Append(c);
                else { sequence.Clear(); state = 0; }
                continue;
            }
            switch (c)
            {
                case '\r': X = 0; wrapPending = false; break;
                case '\n': case '\v': case '\f': LineFeed(); break;
                case '\b': X = Math.Max(0, X - 1); wrapPending = false; break;
                case '\t': X = Math.Min(Columns - 1, (X / 8 + 1) * 8); break;
                default:
                    if (c < ' ') break;
                    if (char.IsHighSurrogate(c)) { highSurrogate = c; break; }
                    var text = char.IsLowSurrogate(c) ? highSurrogate != null ? new string([highSurrogate.Value, c]) : "\ufffd" : c.ToString();
                    highSurrogate = null;
                    int code = char.ConvertToUtf32(text, 0);
                    bool wide = code >= 0x1100 && (code <= 0x115f || code is >= 0x2e80 and <= 0xa4cf || code is >= 0xac00 and <= 0xd7a3 || code is >= 0xf900 and <= 0xfaff || code is >= 0xfe10 and <= 0xfe6f || code is >= 0xff01 and <= 0xff60 || code >= 0x1f300);
                    Put(text, wide ? 2 : 1);
                    break;
            }
        }
    }
    void Put(string text, int width)
    {
        if (wrapPending || X + width > Columns) { X = 0; LineFeed(); }
        Lines[Y][X] = new(text, fg, bg);
        if (width == 2 && X + 1 < Columns) Lines[Y][X + 1] = new("", fg, bg);
        X += width;
        if (X >= Columns) { X = Columns - 1; wrapPending = true; }
    }
    void LineFeed() { wrapPending = false; if (Y == bottom) ScrollUp(); else Y = Math.Min(Rows - 1, Y + 1); }
    void ScrollUp()
    {
        if (!alternate && top == 0) { History.Add(Lines[0]); if (History.Count > 2000) History.RemoveAt(0); }
        for (int y = top; y < bottom; y++) Lines[y] = Lines[y + 1];
        Lines[bottom] = Blank();
    }
    void ScrollDown() { for (int y = bottom; y > top; y--) Lines[y] = Lines[y - 1]; Lines[top] = Blank(); }
    void Clear(int y, int start, int end) { for (int x = Math.Max(0, start); x < Math.Min(Columns, end); x++) Lines[y][x] = Empty; }
    void Execute(char command, string raw)
    {
        bool mode = raw.StartsWith('?');
        int[] p = raw.TrimStart('?', '>').Split(';').Select(s => int.TryParse(s, out var n) ? Math.Clamp(n, 0, 65535) : 0).ToArray();
        int P(int i, int def = 1) => i >= p.Length || p[i] == 0 ? def : p[i];
        wrapPending = false;
        switch (command)
        {
            case 'A': Y = Math.Max(0, Y - P(0)); break;
            case 'B': case 'e': Y = Math.Min(Rows - 1, Y + P(0)); break;
            case 'C': case 'a': X = Math.Min(Columns - 1, X + P(0)); break;
            case 'D': X = Math.Max(0, X - P(0)); break;
            case 'E': X = 0; Y = Math.Min(Rows - 1, Y + P(0)); break;
            case 'F': X = 0; Y = Math.Max(0, Y - P(0)); break;
            case 'G': case '`': X = Math.Clamp(P(0) - 1, 0, Columns - 1); break;
            case 'd': Y = Math.Clamp(P(0) - 1, 0, Rows - 1); break;
            case 'H': case 'f': Y = Math.Clamp(P(0) - 1, 0, Rows - 1); X = Math.Clamp(P(1) - 1, 0, Columns - 1); break;
            case 'J':
                if (p[0] == 3) History.Clear();
                else if (p[0] == 2) { for (int y = 0; y < Rows; y++) Clear(y, 0, Columns); }
                else if (p[0] == 1) { for (int y = 0; y < Y; y++) Clear(y, 0, Columns); Clear(Y, 0, X + 1); }
                else { Clear(Y, X, Columns); for (int y = Y + 1; y < Rows; y++) Clear(y, 0, Columns); }
                break;
            case 'K': Clear(Y, p[0] == 0 ? X : 0, p[0] == 1 ? X + 1 : Columns); break;
            case 'X': Clear(Y, X, X + P(0)); break;
            case 'P': { int n = Math.Min(P(0), Columns - X); Array.Copy(Lines[Y], X + n, Lines[Y], X, Columns - X - n); Clear(Y, Columns - n, Columns); break; }
            case '@': { int n = Math.Min(P(0), Columns - X); Array.Copy(Lines[Y], X, Lines[Y], X + n, Columns - X - n); Clear(Y, X, X + n); break; }
            case 'L': for (int i = 0; i < Math.Min(P(0), Rows); i++) { for (int y = bottom; y > Y; y--) Lines[y] = Lines[y - 1]; Lines[Y] = Blank(); } break;
            case 'M': for (int i = 0; i < Math.Min(P(0), Rows); i++) { for (int y = Y; y < bottom; y++) Lines[y] = Lines[y + 1]; Lines[bottom] = Blank(); } break;
            case 'S': for (int i = 0; i < Math.Min(P(0), Rows); i++) ScrollUp(); break;
            case 'T': for (int i = 0; i < Math.Min(P(0), Rows); i++) ScrollDown(); break;
            case 'r': top = Math.Clamp(P(0) - 1, 0, Rows - 1); bottom = Math.Clamp(P(1, Rows) - 1, top, Rows - 1); X = Y = 0; break;
            case 's': savedX = X; savedY = Y; break;
            case 'u': X = Math.Clamp(savedX, 0, Columns - 1); Y = Math.Clamp(savedY, 0, Rows - 1); break;
            case 'm':
                for (int i = 0; i < p.Length; i++)
                {
                    int n = p[i];
                    if (n == 0) { fg = defaultFg; bg = defaultBg; }
                    else if (n == 7) (fg, bg) = (bg, fg);
                    else if (n == 39) fg = defaultFg;
                    else if (n == 49) bg = defaultBg;
                    else if (n is >= 30 and <= 37) fg = Palette[n - 30];
                    else if (n is >= 40 and <= 47) bg = Palette[n - 40];
                    else if (n is >= 90 and <= 97) fg = Palette[n - 90 + 8];
                    else if (n is >= 100 and <= 107) bg = Palette[n - 100 + 8];
                    else if ((n == 38 || n == 48) && i + 2 < p.Length)
                    {
                        Color color;
                        if (p[i + 1] == 5) { int v = Math.Clamp(p[i + 2], 0, 255); color = v < 16 ? Palette[v] : v >= 232 ? Color.FromArgb(8 + (v - 232) * 10, 8 + (v - 232) * 10, 8 + (v - 232) * 10) : Color.FromArgb(Level((v - 16) / 36), Level((v - 16) / 6 % 6), Level((v - 16) % 6)); i += 2; }
                        else if (p[i + 1] == 2 && i + 4 < p.Length) { color = Color.FromArgb(Math.Min(255, p[i + 2]), Math.Min(255, p[i + 3]), Math.Min(255, p[i + 4])); i += 4; }
                        else break;
                        if (n == 38) fg = color; else bg = color;
                    }
                }
                break;
            case 'h': case 'l':
                if (!mode) break;
                foreach (int n in p)
                {
                    bool enabled = command == 'h';
                    if (n == 25) CursorVisible = enabled;
                    if (n == 1) ApplicationCursor = enabled;
                    if (n == 2004) BracketedPaste = enabled;
                    if (n is 1049 or 47 or 1047 && enabled != alternate)
                    {
                        if (enabled) { primary = Lines; primaryX = X; primaryY = Y; Lines = Enumerable.Range(0, Rows).Select(_ => Blank()).ToArray(); X = Y = 0; }
                        else if (primary != null) { Lines = primary; X = Math.Clamp(primaryX, 0, Columns - 1); Y = Math.Clamp(primaryY, 0, Rows - 1); primary = null; }
                        alternate = enabled; top = 0; bottom = Rows - 1;
                    }
                }
                break;
            case 'n': if (p[0] == 6) Reply?.Invoke($"\x1b[{Y + 1};{X + 1}R"); else if (p[0] == 5) Reply?.Invoke("\x1b[0n"); break;
            case 'c': Reply?.Invoke("\x1b[?1;2c"); break;
        }
    }
    static int Level(int n) => n == 0 ? 0 : n * 40 + 55;
    public string Text => string.Join("\r\n", History.Concat(Lines).Select(row => string.Concat(row.Select(c => c.Text)).TrimEnd()));
}
