namespace PortHarbor;

internal static class SessionTreeIcons
{
    internal static ImageList Create(UiPalette? palette = null, int dpi = 96)
    {
        palette ??= UiPalette.Resolve(InterfaceTheme.Light, SystemInformation.HighContrast);
        int size = Math.Max(20, (int)Math.Round(20 * dpi / 96d));
        var images = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit };
        _ = images.Handle; // Materialize before temporary bitmaps are disposed.
        foreach (var key in new[] { "folder", "terminal", "files", "desktop" })
        {
            using var bitmap = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size / 20f, size / 20f);
            using var edge = new Pen(palette.Muted, 1.4f);
            if (key == "folder")
            {
                using var fill = new SolidBrush(palette.HighContrast ? palette.Surface : Color.FromArgb(239, 183, 65));
                graphics.FillRectangle(fill, 2, 4, 7, 4); graphics.FillRectangle(fill, 2, 7, 16, 10);
                graphics.DrawRectangle(edge, 2, 7, 16, 10);
            }
            else
            {
                using var fill = new SolidBrush(palette.HighContrast ? palette.Surface : palette.Accent);
                graphics.FillRectangle(fill, 2, 3, 16, 12); graphics.DrawRectangle(edge, 2, 3, 16, 12);
                using var ink = new Pen(palette.HighContrast ? palette.Text : palette.OnAccent, 1.4f);
                if (key == "files") { graphics.DrawLine(ink, 6, 7, 14, 7); graphics.DrawLine(ink, 6, 11, 14, 11); }
                else if (key == "terminal") { graphics.DrawLines(ink, new Point[] { new Point(5, 6), new Point(8, 9), new Point(5, 12) }); graphics.DrawLine(ink, 10, 12, 14, 12); }
                else { graphics.DrawLine(edge, 10, 15, 10, 18); graphics.DrawLine(edge, 5, 18, 15, 18); }
            }
            images.Images.Add(key, bitmap);
        }
        return images;
    }
}

