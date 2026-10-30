using PortHarbor;

namespace PortHarbor.Checks;

internal static class TerminalModelChecks
{
    internal static void Run(Action<bool, string> Check)
    {
        var t = new TerminalScreen(12, 4); t.Feed("abc\rZ"); Check(t.Lines[0][0].Text == "Z" && t.Lines[0][1].Text == "b", "carriage return overwrites cursor position");
        t.Feed("\x1b[2;3H\x1b[31m红"); Check(t.Lines[1][2].Text == "红" && t.Lines[1][3].Text == "" && t.Lines[1][2].Foreground != TerminalScreen.DefaultForeground, "CJK uses two cells with ANSI color");
        t.Feed("\x1b[2J\x1b[Hmain\x1b[?1049hALT\x1b[?1049l"); Check(t.Lines[0][0].Text == "m", "alternate buffer restores primary screen");
        t.Feed("\x1b[3;1Hold\x1b[3;1H\x1b[Knew"); Check(string.Concat(t.Lines[2].Select(c => c.Text)).TrimEnd() == "new", "erase line clears stale content");
        string? reply = null; t.Reply += s => reply = s; t.Feed("\x1b[6n"); Check(reply == "\x1b[3;4R", "terminal reports cursor position");
        t.Feed("\x1b[38;2;10;20;30mX"); Check(t.Lines[2][3].Foreground == System.Drawing.Color.FromArgb(10, 20, 30), "truecolor SGR");
        t.Feed("\x1b]0;invisible title\aQ"); Check(!t.Text.Contains("invisible title"), "OSC title does not leak into screen");
        t.Resize(20, 8); Check(t.Columns == 20 && t.Rows == 8 && t.Lines[0][0].Text == "m", "resize preserves screen contents");
        t.Feed("\x1b[?2004h\x1b[?1h"); Check(t.BracketedPaste && t.ApplicationCursor, "bracketed paste and application cursor modes");
        t.Feed("\ud83d"); t.Feed("\ude00"); Check(t.Text.Contains("😀"), "split surrogate pair handled");
        var random = new Random(42); for (int i = 0; i < 2000; i++) t.Feed($"\x1b[{random.Next(65535)};{random.Next(65535)}{new[] { 'H', 'J', 'K', 'm', 'P', '@', 'L', 'M', 'r' }[random.Next(9)]}");
        Check(t.X < t.Columns && t.Y < t.Rows, "malformed and oversized ANSI parameters stay bounded");
    }
}
