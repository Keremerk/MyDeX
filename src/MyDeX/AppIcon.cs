using System.Drawing.Drawing2D;

namespace MyDeX;

/// <summary>Draws the MyDeX icon (a small monitor) so the app needs no .ico file.</summary>
static class AppIcon
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var blue = new SolidBrush(Color.FromArgb(22, 110, 230));
            using var white = new SolidBrush(Color.White);
            using var screen = RoundedRect(new Rectangle(2, 4, 28, 19), 4);
            g.FillPath(blue, screen);
            g.FillRectangle(white, 6, 8, 20, 11);
            g.FillRectangle(blue, 13, 23, 6, 4);
            g.FillRectangle(blue, 9, 27, 14, 3);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
