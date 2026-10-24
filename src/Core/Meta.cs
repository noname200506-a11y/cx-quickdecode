// V3 元数据层：外扩边框 LSB 里的「区域表 + 签名 + 标志」
// 逐位等价移植自 cx-E2EE src/image-crypto.js：metaBytes / parseMeta / borderOrd / wMetaPng / rMetaPng
namespace CxQuickDecode.Core;

internal sealed class MetaInfo
{
    public int Count;
    public List<(int x, int y, int w, int h)> Regions = new();
    public int Stored;      // 图片里存的签名（24bit）
    public int Flag;
    public int DataLen;
    /// <summary>bit0=诱饵图，bit1=色块排序，bit2-3=诱饵降采样率-1，bit3=JPEG 友好亮度域(soft)。</summary>
    public bool Decoy => (Flag & 1) != 0;
    public bool Sort => (Flag & 2) != 0;
    public int DecoyScale => ((Flag >> 2) & 63) == 0 ? 1 : ((Flag >> 2) & 63);
    public bool Soft => (Flag & 8) != 0;
}

internal static class Meta
{
    public const int BorderPng = 8;      // PNG 路径边框
    public const int BorderJpegOld = 32; // 旧 JPEG 块级路径
    public const int MaxBytes = 200;     // 网页端 rMetaPng 读 200 字节
    public const int MaxRegions = 12;

    /// <summary>边框像素索引流：顶 → 右 → 底 → 左（与 JS borderOrd 完全一致）。</summary>
    public static int[] BorderOrder(int cw, int ch, int B)
    {
        var pts = new List<int>(2 * (cw + ch) * B);
        for (int x = 0; x < cw; x++)
            for (int y = 0; y < B; y++) pts.Add(y * cw + x);
        for (int y = B; y < ch - B; y++)
            for (int x = cw - B; x < cw; x++) pts.Add(y * cw + x);
        for (int x = cw - 1; x >= 0; x--)
            for (int y = ch - B; y < ch; y++) pts.Add(y * cw + x);
        for (int y = ch - B - 1; y >= B; y--)
            for (int x = 0; x < B; x++) pts.Add(y * cw + x);
        return pts.ToArray();
    }

    /// <summary>rMetaPng：读 R 通道 LSB → 字节流。</summary>
    public static byte[] ReadMetaPng(Rgba img, int[] order, int maxBytes = MaxBytes)
    {
        int n = Math.Min(order.Length, maxBytes * 8);
        var bits = new byte[n];
        var d = img.Data;
        int w = img.W;
        for (int i = 0; i < n; i++)
        {
            int p = order[i];
            int x = p % w, y = p / w;
            bits[i] = (byte)(d[(y * w + x) * 4] & 1);
        }
        var bytes = new byte[n / 8];
        for (int i = 0; i + 8 <= n; i += 8)
        {
            int b = 0;
            for (int j = 0; j < 8; j++) b = (b << 1) | bits[i + j];
            bytes[i / 8] = (byte)b;
        }
        return bytes;
    }

    /// <summary>wMetaPng：每字节写 8 个像素的 R 通道最低位（高位在前）。</summary>
    public static void WriteMetaPng(Rgba img, int[] order, byte[] bs)
    {
        var d = img.Data;
        int w = img.W;
        int bi = 0;
        foreach (byte b in bs)
            for (int i = 7; i >= 0; i--)
            {
                if (bi >= order.Length) return;
                int p = order[bi++];
                int x = p % w, y = p / w;
                int f = (y * w + x) * 4;
                d[f] = (byte)((d[f] & 254) | ((b >> i) & 1));
            }
    }

    public static MetaInfo? Parse(byte[] bs)
    {
        if (bs == null || bs.Length < 5) return null;
        int cnt = bs[0];
        if (cnt < 1 || cnt > MaxRegions) return null;
        if (bs[1 + cnt * 8] != 67 || bs[2 + cnt * 8] != 88) return null;   // "CX"
        var info = new MetaInfo { Count = cnt };
        for (int i = 0; i < cnt; i++)
        {
            int o = 1 + i * 8;
            info.Regions.Add((
                bs[o] * 256 + bs[o + 1],
                bs[o + 2] * 256 + bs[o + 3],
                bs[o + 4] * 256 + bs[o + 5],
                bs[o + 6] * 256 + bs[o + 7]));
        }
        int so = 1 + cnt * 8 + 2;
        info.Stored = (bs[so] << 16) | (bs[so + 1] << 8) | bs[so + 2];
        info.Flag = bs[so + 3] & 255;
        if (bs.Length > so + 7)
            info.DataLen = (bs[so + 4] << 24) | (bs[so + 5] << 16) | (bs[so + 6] << 8) | bs[so + 7];
        return info;
    }

    /// <summary>从带边框的画布读元数据（PNG 8px 路径）。</summary>
    public static MetaInfo? ReadFromImage(Rgba img, int B = BorderPng)
    {
        var order = BorderOrder(img.W, img.H, B);
        return Parse(ReadMetaPng(img, order));
    }

    /// <summary>网页端的 flag bit3 = JPEG 友好亮度域（soft），仅 JPEG 路径为 1。</summary>
    public static bool VerifyPassword(MetaInfo info, string password)
        => (CxHash.Signature(password, info.Count, info.Regions) & 0xFFFFFF) == info.Stored;

    /// <summary>把外扩边框画布重建出来（打码用；边缘按网页端横向拉伸）。</summary>
    public static Rgba PadWithBorder(Rgba src, int B)
    {
        int w = src.W, h = src.H;
        int cw = w + 2 * B, ch = h + 2 * B;
        var outImg = new Rgba(cw, ch);
        var s = src.Data;
        var d = outImg.Data;

        // 上/下：第 0 行 / 最后一行横向铺满
        for (int x = 0; x < cw; x++)
        {
            int sxi = Math.Clamp(x - B, 0, w - 1);
            for (int y = 0; y < B; y++)
                CopyPix(s, (0 * w + sxi) * 4, d, (y * cw + x) * 4);
            for (int y = 0; y < B; y++)
                CopyPix(s, ((h - 1) * w + sxi) * 4, d, ((ch - B + y) * cw + x) * 4);
        }
        // 左/右：第 0 列 / 最后一列纵向拉伸
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < B; x++)
            {
                CopyPix(s, (y * w + 0) * 4, d, ((B + y) * cw + x) * 4);
                CopyPix(s, (y * w + w - 1) * 4, d, ((B + y) * cw + cw - B + x) * 4);
            }
        }
        // 原图居中
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(s, y * w * 4, d, ((B + y) * cw + B) * 4, w * 4);
        return outImg;
    }

    private static void CopyPix(byte[] s, int si, byte[] d, int di)
    {
        d[di] = s[si]; d[di + 1] = s[si + 1]; d[di + 2] = s[si + 2]; d[di + 3] = 255;
    }
}
