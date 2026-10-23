namespace PortHarbor;

internal static class SessionTreeIcons
{
    internal static ImageList Create()
    {
        var images = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };
        _ = images.Handle; // Materialize before temporary bitmaps are disposed.
        foreach (var key in new[] { "folder", "terminal", "files", "desktop" })
        {
            using var bitmap = new Bitmap(20, 20);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var edge = new Pen(Color.FromArgb(65, 90, 120), 1.4f);
            if (key == "folder")
            {
                using var fill = new SolidBrush(Color.FromArgb(239, 183, 65));
                graphics.FillRectangle(fill, 2, 4, 7, 4); graphics.FillRectangle(fill, 2, 7, 16, 10);
                graphics.DrawRectangle(edge, 2, 7, 16, 10);
            }
            else
            {
                using var fill = new SolidBrush(key == "files" ? Color.FromArgb(68, 151, 116) : Color.FromArgb(53, 83, 126));
                graphics.FillRectangle(fill, 2, 3, 16, 12); graphics.DrawRectangle(edge, 2, 3, 16, 12);
                using var ink = new Pen(Color.White, 1.4f);
                if (key == "files") { graphics.DrawLine(ink, 6, 7, 14, 7); graphics.DrawLine(ink, 6, 11, 14, 11); }
                else if (key == "terminal") { graphics.DrawLines(ink, new Point[] { new Point(5, 6), new Point(8, 9), new Point(5, 12) }); graphics.DrawLine(ink, 10, 12, 14, 12); }
                else { graphics.DrawLine(edge, 10, 15, 10, 18); graphics.DrawLine(edge, 5, 18, 15, 18); }
            }
            images.Images.Add(key, bitmap);
        }
        return images;
    }
}

