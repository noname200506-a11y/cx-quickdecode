// 剪贴板取图：直读原生剪贴板（不经过 OLE / GDI+ 的位图转换，那会改掉边框 LSB）
//   优先级：① "PNG" 原始字节（浏览器复制图片时最保真）② CF_DIBV5 / CF_DIB 原始 DIB
//           ③ CF_HDROP 里的第一个图片文件 ④ 兜底 Windows Forms Clipboard.GetImage()
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal sealed class ClipboardImage
{
    public Rgba? Image;
    public string Desc = "";
    public string Source = "";
    /// <summary>true = 拿到的是逐字节无损的源（文件路径 / PNG 原始字节）</summary>
    public bool Lossless;
    /// <summary>若来自剪贴板的文件列表，这里是那个文件路径</summary>
    public string? FilePath;
}

internal static class ClipboardReader
{
    public static ClipboardImage ReadFirstImage()
    {
        var res = new ClipboardImage();
        string why = "剪贴板里没有图片（先复制一张图，或按 Win+Shift+S 截图）";

        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (!Native.OpenClipboard(IntPtr.Zero)) { Thread.Sleep(50); continue; }
            try
            {
                // ① 文件列表（CF_HDROP）—— 最优先：文件是逐字节无损的。
                //    QQ/微信/资源管理器「复制图片」经常同时给 FileDrop + 位图；位图那一路
                //    已被 Windows 转换过，最低位（元数据）已经毁了，先用位图就一定失败。
                if (Native.IsClipboardFormatAvailable(Native.CF_HDROP))
                {
                    IntPtr hDrop = Native.GetClipboardData(Native.CF_HDROP);
                    if (hDrop != IntPtr.Zero)
                    {
                        uint count = Native.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                        for (uint i = 0; i < count; i++)
                        {
                            var sb = new StringBuilder(1024);
                            Native.DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                            string f = sb.ToString();
                            if (!ImageIo.IsImagePath(f)) continue;
                            try
                            {
                                res.Image = ImageIo.LoadFile(f);
                                res.Source = "剪贴板文件（无损）";
                                res.Desc = $"剪贴板文件 {Path.GetFileName(f)} {res.Image.W}×{res.Image.H}";
                                res.Lossless = true;
                                res.FilePath = f;
                                return res;
                            }
                            catch { /* 文件读不了就继续试别的格式 */ }
                        }
                        why = "剪贴板里的文件不是图片";
                    }
                }

                // ② PNG 原始字节：无损，元数据在
                uint pngFmt = Native.RegisterClipboardFormat("PNG");
                uint webpFmt = Native.RegisterClipboardFormat("image/png");
                foreach (uint fmt in new[] { pngFmt, webpFmt })
                {
                    if (fmt == 0 || !Native.IsClipboardFormatAvailable(fmt)) continue;
                    var bytes = Native.ClipboardBytes(fmt);
                    if (bytes == null || !PngCodec.LooksLikePng(bytes)) continue;
                    res.Image = PngCodec.Decode(bytes);
                    res.Source = "剪贴板 PNG（无损）";
                    res.Desc = $"剪贴板 PNG {res.Image.W}×{res.Image.H}";
                    res.Lossless = true;
                    return res;
                }

                // ② 原始 DIB（DIBV5 优先：32bpp 带 alpha）
                foreach (uint fmt in new[] { Native.CF_DIBV5, Native.CF_DIB })
                {
                    if (!Native.IsClipboardFormatAvailable(fmt)) continue;
                    var bytes = Native.ClipboardBytes(fmt);
                    if (bytes == null) continue;
                    if (TryDecodeDib(bytes, out var img, out string err) && img != null)
                    {
                        res.Image = img;
                        res.Source = fmt == Native.CF_DIBV5 ? "剪贴板 DIBV5" : "剪贴板 DIB";
                        res.Desc = $"剪贴板位图 {img.W}×{img.H}";
                        return res;
                    }
                    why = "剪贴板位图解析失败：" + err;
                }

                res.Desc = why;
                return res;
            }
            catch (Exception ex) { why = "读剪贴板失败：" + ex.Message; }
            finally { Native.CloseClipboard(); }
        }

        // ④ 兜底（会经过 GDI+，LSB 可能已经没了，只用来"至少能看"）
        try
        {
            Image? bmp = Clipboard.GetImage();
            if (bmp != null)
            {
                using var asBmp = new Bitmap(bmp);
                res.Image = ImageIo.FromBitmap(asBmp);
                res.Source = "剪贴板位图（GDI+ 转换）";
                res.Desc = $"剪贴板位图 {res.Image.W}×{res.Image.H}（可能已丢失最低位）";
                return res;
            }
        }
        catch { /* 剪贴板被占用 */ }

        res.Desc = why;
        return res;
    }

    /// <summary>只取剪贴板文件列表里的第一个图片路径（截图解码失败时兜底用）。</summary>
    public static string? TryGetFirstImageFile()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!Native.OpenClipboard(IntPtr.Zero)) { Thread.Sleep(40); continue; }
            try
            {
                if (!Native.IsClipboardFormatAvailable(Native.CF_HDROP)) return null;
                IntPtr hDrop = Native.GetClipboardData(Native.CF_HDROP);
                if (hDrop == IntPtr.Zero) return null;
                uint count = Native.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count; i++)
                {
                    var sb = new StringBuilder(1024);
                    Native.DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                    string f = sb.ToString();
                    if (ImageIo.IsImagePath(f)) return f;
                }
                return null;
            }
            catch { return null; }
            finally { Native.CloseClipboard(); }
        }
        return null;
    }

    /// <summary>把结果图放回剪贴板（PNG 字节 + 位图两种格式）。</summary>
    public static bool PutImage(Rgba img)
    {
        try
        {
            var png = PngCodec.Encode(img);
            var data = new DataObject();
            var bmp = ImageIo.ToBitmap(img);
            data.SetData(DataFormats.Bitmap, true, bmp);
            data.SetData("PNG", false, new MemoryStream(png));
            Clipboard.SetDataObject(data, true, 8, 120);
            return true;
        }
        catch { return false; }
    }

    /// <summary>原始 DIB（BITMAPINFOHEADER / V4 / V5）→ Rgba。支持 32/24/16 bpp，上/下两种行序。</summary>
    public static bool TryDecodeDib(byte[] dib, out Rgba? img, out string err)
    {
        img = null; err = "";
        try
        {
            if (dib.Length < 40) { err = "DIB 太短"; return false; }
            int biSize = BitConverter.ToInt32(dib, 0);
            if (biSize != 40 && biSize != 108 && biSize != 124) { err = $"DIB 头长度异常 {biSize}"; return false; }
            int w = BitConverter.ToInt32(dib, 4);
            int hRaw = BitConverter.ToInt32(dib, 8);
            int planes = BitConverter.ToInt16(dib, 12);
            int bitCount = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            bool topDown = hRaw < 0;
            int h = Math.Abs(hRaw);
            if (w <= 0 || h <= 0 || w > 20000 || h > 20000) { err = $"DIB 尺寸非法 {w}x{h}"; return false; }
            if (planes != 1) { err = "DIB planes!=1"; return false; }
            if (compression != 0 && compression != 3) { err = $"DIB 压缩方式 {compression} 不支持"; return false; }
            if (bitCount != 32 && bitCount != 24 && bitCount != 16) { err = $"DIB 位深 {bitCount} 不支持"; return false; }

            int paletteSize = 0;
            if (bitCount <= 8)
            {
                int clrUsed = BitConverter.ToInt32(dib, 32);
                paletteSize = (clrUsed > 0 ? clrUsed : (1 << bitCount)) * 4;
            }
            else if (compression == 3 && biSize == 40)
            {
                paletteSize = 12;          // 三个 DWORD 掩码
            }
            int offBits = biSize + paletteSize;
            int stride = ((w * bitCount + 31) / 32) * 4;
            long need = (long)offBits + (long)stride * (h - 1) + 1;
            if (dib.Length < need) { err = $"DIB 数据不完整（需要 {need}，实际 {dib.Length}）"; return false; }

            img = new Rgba(w, h);
            var d = img.Data;
            bool alphaAllZero = true;
            for (int y = 0; y < h; y++)
            {
                int srcRow = topDown ? y : (h - 1 - y);
                int ro = offBits + srcRow * stride;
                int o = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int b, g, r, a = 255;
                    if (bitCount == 32)
                    {
                        b = dib[ro + x * 4]; g = dib[ro + x * 4 + 1]; r = dib[ro + x * 4 + 2];
                        a = dib[ro + x * 4 + 3];
                        if (a != 0) alphaAllZero = false;
                    }
                    else if (bitCount == 24)
                    {
                        b = dib[ro + x * 3]; g = dib[ro + x * 3 + 1]; r = dib[ro + x * 3 + 2];
                    }
                    else
                    {
                        int v = dib[ro + x * 2] | (dib[ro + x * 2 + 1] << 8);
                        int r5 = (v >> 10) & 0x1F, g5 = (v >> 5) & 0x1F, b5 = v & 0x1F;
                        r = (r5 << 3) | (r5 >> 2); g = (g5 << 3) | (g5 >> 2); b = (b5 << 3) | (b5 >> 2);
                    }
                    d[o + x * 4] = (byte)r;
                    d[o + x * 4 + 1] = (byte)g;
                    d[o + x * 4 + 2] = (byte)b;
                    d[o + x * 4 + 3] = (byte)a;
                }
            }
            if (alphaAllZero)   // 很多 DIB 不带 alpha 通道，全 0 → 当不透明
                for (int i = 3; i < d.Length; i += 4) d[i] = 255;
            return true;
        }
        catch (Exception ex) { err = ex.Message; return false; }
    }
}
