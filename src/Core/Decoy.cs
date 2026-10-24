// 诱饵模式（flag bit0）：区域原始像素按 24bit/像素散布在原图区 LSB 里，按行优先取回
namespace CxQuickDecode.Core;

internal static class Decoy
{
    /// <summary>JS P.decoyExtract 的等价实现。sc = 降采样率（1 = 原始）。</summary>
    public static bool DecoyExtract(Rgba img, int dB, IReadOnlyList<(int x, int y, int w, int h)> regions,
                                    int dataLen, int sc)
    {
        int w = img.W - dB * 2, h = img.H - dB * 2;
        if (w <= 0 || h <= 0 || dataLen <= 0) return false;
        var d = img.Data;
        var bits = new byte[dataLen];
        int bi = 0;
        bool done = false;
        for (int y = 0; y < h && !done; y++)
        {
            for (int l = 0; l < w; l++)
            {
                int f = ((dB + y) * img.W + (dB + l)) * 4;
                for (int c = 0; c < 3; c++)
                {
                    if (bi >= dataLen) { done = true; break; }
                    bits[bi++] = (byte)(d[f + c] & 1);
                }
                if (done) break;
            }
        }
        if (bi < dataLen) return false;

        int ByteAt(int byteIndex)
        {
            int b0 = byteIndex * 8;
            if (b0 + 7 >= bits.Length) return 0;
            int v = 0;
            for (int k = 0; k < 8; k++) v = (v << 1) | bits[b0 + k];
            return v;
        }

        int off = 0;
        foreach (var reg in regions)
        {
            int sw = (int)Math.Ceiling(reg.w / (double)sc);
            if (sw < 1) sw = 1;
            int sh = (int)Math.Ceiling(reg.h / (double)sc);
            if (sh < 1) sh = 1;
            for (int sy = 0; sy < sh; sy++)
                for (int sx = 0; sx < sw; sx++)
                {
                    int R = ByteAt(off), G = ByteAt(off + 1), B = ByteAt(off + 2);
                    off += 3;
                    for (int yy = 0; yy < sc; yy++)
                        for (int xx = 0; xx < sc; xx++)
                        {
                            int px = reg.x + sx * sc + xx, py = reg.y + sy * sc + yy;
                            if (px >= reg.x + reg.w || py >= reg.y + reg.h) continue;
                            int tx = dB + px, ty = dB + py;
                            if (tx < 0 || ty < 0 || tx >= img.W || ty >= img.H) continue;
                            int f = (ty * img.W + tx) * 4;
                            d[f] = (byte)R; d[f + 1] = (byte)G; d[f + 2] = (byte)B;
                        }
                }
        }
        return true;
    }
}
