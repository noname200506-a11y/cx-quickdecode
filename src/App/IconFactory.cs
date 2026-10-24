// 程序图标（代码生成，不依赖外部资源文件 → 单文件 exe 便携）
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CxQuickDecode.App;

internal static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon? _app;

    public static Icon AppIcon => _app ??= Build();

    private static Icon Build()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(47, 108, 235));
            using var path = new GraphicsPath();
            var r = new Rectangle(2, 2, 28, 28);
            path.AddArc(r.Left, r.Top, 8, 8, 180, 90);
            path.AddArc(r.Right - 8, r.Top, 8, 8, 270, 90);
            path.AddArc(r.Right - 8, r.Bottom - 8, 8, 8, 0, 90);
            path.AddArc(r.Left, r.Bottom - 8, 8, 8, 90, 90);
            path.CloseFigure();
            g.FillPath(bg, path);

            // 锁形：代表「有密码的图」
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, 10, 15, 12, 9);
            using var pen = new Pen(Color.White, 2f);
            g.DrawArc(pen, 12, 9, 8, 10, 180, 180);
            using var hole = new SolidBrush(Color.FromArgb(47, 108, 235));
            g.FillEllipse(hole, 14, 18, 4, 4);
        }
        IntPtr h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
        finally { DestroyIcon(h); }
    }
}
