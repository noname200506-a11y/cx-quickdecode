// 图像 I/O：PNG 走自写编解码（逐字节保真），其它格式退回 GDI+（只用于预览 / JPEG）
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal static class ImageIo
{
    public static readonly string[] Exts =
    {
        ".png", ".PNG", ".jpg", ".jpeg", ".JPG", ".JPEG", ".bmp", ".gif", ".webp", ".tif", ".tiff"
    };

    public static bool IsImagePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) return false;
        string e = Path.GetExtension(p);
        foreach (var x in Exts) if (string.Equals(e, x, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>读文件 → Rgba。PNG 用自写解码器（保 LSB）；其它格式用 GDI+。</summary>
    public static Rgba LoadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (PngCodec.LooksLikePng(bytes)) return PngCodec.Decode(bytes);
        using var bmp = new Bitmap(new MemoryStream(bytes));
        return FromBitmap(bmp);
    }

    public static Rgba FromBitmap(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var img = new Rgba(w, h);
        var data = img.Data;
        if (bmp.PixelFormat == PixelFormat.Format32bppArgb || bmp.PixelFormat == PixelFormat.Format32bppPArgb
            || bmp.PixelFormat == PixelFormat.Format32bppRgb)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[bd.Stride];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, row, 0, row.Length);
                    int o = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        data[o + x * 4] = row[x * 4 + 2];
                        data[o + x * 4 + 1] = row[x * 4 + 1];
                        data[o + x * 4 + 2] = row[x * 4];
                        data[o + x * 4 + 3] = row[x * 4 + 3];
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }
            return img;
        }
        // 兜底：逐像素 GetPixel（慢但正确）
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = bmp.GetPixel(x, y);
                int o = (y * w + x) * 4;
                data[o] = c.R; data[o + 1] = c.G; data[o + 2] = c.B; data[o + 3] = c.A;
            }
        return img;
    }

    public static Bitmap ToBitmap(Rgba img)
    {
        var bmp = new Bitmap(img.W, img.H, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, img.W, img.H), ImageLockMode.WriteOnly,
                              PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bd.Stride];
            var src = img.Data;
            for (int y = 0; y < img.H; y++)
            {
                int o = y * img.W * 4;
                for (int x = 0; x < img.W; x++)
                {
                    row[x * 4] = src[o + x * 4 + 2];
                    row[x * 4 + 1] = src[o + x * 4 + 1];
                    row[x * 4 + 2] = src[o + x * 4];
                    row[x * 4 + 3] = src[o + x * 4 + 3];
                }
                Marshal.Copy(row, 0, bd.Scan0 + y * bd.Stride, row.Length);
            }
        }
        finally { bmp.UnlockBits(bd); }
        return bmp;
    }

    /// <summary>BGRA（屏幕 DIB）→ Rgba。</summary>
    public static Rgba FromBgra(byte[] bgra, int w, int h)
    {
        var img = new Rgba(w, h);
        var d = img.Data;
        for (int i = 0, n = w * h; i < n; i++)
        {
            int s = i * 4;
            d[s] = bgra[s + 2];
            d[s + 1] = bgra[s + 1];
            d[s + 2] = bgra[s];
            d[s + 3] = 255;
        }
        return img;
    }

    public static byte[] EncodePng(Rgba img) => PngCodec.Encode(img);

    /// <summary>缩放成缩略图（等比，最长边 maxSide）。</summary>
    public static Rgba Thumb(Rgba img, int maxSide)
    {
        int w = img.W, h = img.H;
        if (Math.Max(w, h) <= maxSide) return img;
        double s = (double)maxSide / Math.Max(w, h);
        return img.Scale(Math.Max(1, (int)Math.Round(w * s)), Math.Max(1, (int)Math.Round(h * s)));
    }

    /// <summary>结果自动命名（另存为对话框的默认名）。</summary>
    public static string SuggestedName(string prefix)
        => $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
}
