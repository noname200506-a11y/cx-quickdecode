// 区域块置换 + YUV 扰动（IA.regionEnc 的等价实现，含 sortRegion）
// 来源：cx-E2EE src/image-crypto.js P.regionEnc / P.sortRegion
namespace CxQuickDecode.Core;

internal static class RegionEnc
{
    private const double CM50 = 50.0, CM155 = 155.0;
    private const double SM20 = 20.0, SM215 = 215.0;

    /// <summary>
    /// 对 img 的指定区域执行 / 逆执行区域混淆（就地）。
    /// encode=true 打码，false 还原；unit=8（PNG 路径）/ 16（JPEG 拍屏路径）；
    /// soft=JPEG 友好亮度域；sort=色块亮度排序（PNG 增强，加密末尾 / 解密开头）。
    /// 返回 false 表示区域退化（太小 / 全在画布外）。
    /// </summary>
    public static bool Apply(Rgba img, int rx, int ry, int rw, int rh, int seed,
                             bool encode, bool sort, bool soft, int unit)
    {
        if (unit <= 0) throw new ArgumentException("unit 必须为正");
        int W = img.W, H = img.H;
        int x0 = (int)Math.Floor(rx / (double)unit) * unit;
        int y0 = (int)Math.Floor(ry / (double)unit) * unit;
        int x1 = Math.Min(W, (int)(Math.Ceiling((rx + rw) / (double)unit) * unit));
        int y1 = Math.Min(H, (int)(Math.Ceiling((ry + rh) / (double)unit) * unit));
        x1 = x0 + (int)Math.Floor((x1 - x0) / (double)unit) * unit;
        y1 = y0 + (int)Math.Floor((y1 - y0) / (double)unit) * unit;
        if (x1 <= x0 || y1 <= y0) return false;
        // 与画布相交性（JS 里 x 可能为负：floor 后为负，索引会越界 —— 这里按画布钳制）
        if (x1 <= 0 || y1 <= 0 || x0 >= W || y0 >= H) return false;
        int cx0 = Math.Max(0, x0), cy0 = Math.Max(0, y0);
        int cx1 = Math.Min(W, x1), cy1 = Math.Min(H, y1);
        if (cx1 <= cx0 || cy1 <= cy0) return false;

        int bx = (x1 - x0) / unit;
        int by = (y1 - y0) / unit;
        int bl = bx * by;
        if (bl <= 0) return false;

        var p = CxPerm.Perm(bl, seed);
        var cm = CxPerm.Cm(bl, seed);

        if (!encode && sort) SortRegion(img, x0, y0, bx, by, bl, false);

        var d = img.Data;
        // 目标缓冲只覆盖区域（等价 JS 对整幅 out 的复制：区域外不变）
        int aw = cx1 - cx0, ah = cy1 - cy0;
        var outBuf = new byte[aw * ah * 4];

        for (int i = 0; i < bl; i++)
        {
            int si = encode ? i : p[i];
            int di = encode ? p[i] : i;
            int sCol = si % bx, sRow = si / bx;
            int dCol = di % bx, dRow = di / bx;
            int sx = x0 + sCol * unit, sy = y0 + sRow * unit;
            int dx = x0 + dCol * unit, dy = y0 + dRow * unit;
            int rwv = cm[encode ? di : si];
            int ns = soft ? (rwv % 33) - 16 : (rwv % 81) - 40;
            bool fU = (rwv & 0x100) != 0, fV = (rwv & 0x200) != 0, sw = (rwv & 0x400) != 0;

            for (int y = 0; y < unit; y++)
            {
                int py = sy + y;
                if (py < 0 || py >= H) continue;
                for (int l = 0; l < unit; l++)
                {
                    int px = sx + l;
                    if (px < 0 || px >= W) continue;
                    int sI = (py * W + px) * 4;
                    int R = d[sI], G = d[sI + 1], B = d[sI + 2];
                    double Y = .299 * R + .587 * G + .114 * B;
                    double U = -.147 * R - .289 * G + .436 * B;
                    double V = .615 * R - .515 * G - .1 * B;
                    if (encode)
                    {
                        Y = (soft ? SM20 + (Y / 255.0) * SM215 : CM50 + (Y / 255.0) * CM155) + ns;
                        if (sw) (U, V) = (V, U);
                        if (fU) U = -U;
                        if (fV) V = -V;
                    }
                    else
                    {
                        if (fV) V = -V;
                        if (fU) U = -U;
                        if (sw) (U, V) = (V, U);
                        Y -= ns;
                        Y = soft
                            ? Math.Clamp((Y - SM20) / SM215, 0.0, 1.0) * 255.0
                            : Math.Clamp((Y - CM50) / CM155, 0.0, 1.0) * 255.0;
                    }
                    double R2 = Y + 1.14 * V;
                    double G2 = Y - .395 * U - .581 * V;
                    double B2 = Y + 2.032 * U;

                    int tx = dx + l, ty = dy + y;
                    if (tx < cx0 || tx >= cx1 || ty < cy0 || ty >= cy1) continue;
                    int oi = ((ty - cy0) * aw + (tx - cx0)) * 4;
                    outBuf[oi] = Js.ClampU8(R2);
                    outBuf[oi + 1] = Js.ClampU8(G2);
                    outBuf[oi + 2] = Js.ClampU8(B2);
                    outBuf[oi + 3] = 255;
                }
            }
        }

        // 写回区域
        for (int y = 0; y < ah; y++)
            Buffer.BlockCopy(outBuf, y * aw * 4, d, ((cy0 + y) * W + cx0) * 4, aw * 4);

        if (encode && sort) SortRegion(img, x0, y0, bx, by, bl, true);
        return true;
    }

    /// <summary>
    /// IA.sortRegion：区域内块按亮度升序重排（enc 时把原索引写进每块首像素 R 的 LSB，dec 时读回）。
    /// 块恒为 8×8（仅 PNG 路径使用）。
    /// </summary>
    internal static void SortRegion(Rgba img, int x0, int y0, int bx, int by, int bl, bool enc)
    {
        const int U = 8;
        int W = img.W, H = img.H;
        var d = img.Data;
        int rbit = (int)Math.Ceiling(Math.Log2(Math.Max(bl, 1)));
        if (rbit < 1) rbit = 1;

        if (enc)
        {
            var lum = new double[bl];
            for (int i = 0; i < bl; i++)
            {
                int bx0 = x0 + (i % bx) * U, by0 = y0 + (i / bx) * U;
                double s = 0;
                for (int y = 0; y < U; y++)
                    for (int l = 0; l < U; l++)
                    {
                        int f = ((by0 + y) * W + bx0 + l) * 4;
                        s += d[f] * .299 + d[f + 1] * .587 + d[f + 2] * .114;
                    }
                lum[i] = s / 64.0;
            }
            // JS Array.sort 稳定；C# Array.Sort 不稳定 → 用 OrderBy（稳定升序）
            var ord = new int[bl];
            for (int i = 0; i < bl; i++) ord[i] = i;
            ord = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.OrderBy(ord, i => lum[i]));

            var tmp = (byte[])d.Clone();
            for (int j = 0; j < bl; j++)
            {
                int src = ord[j];
                int sBlockX = x0 + (src % bx) * U, sBlockY = y0 + (src / bx) * U;
                int dBlockX = x0 + (j % bx) * U, dBlockY = y0 + (j / bx) * U;
                for (int y = 0; y < U; y++)
                    for (int l = 0; l < U; l++)
                    {
                        int sf = ((sBlockY + y) * W + sBlockX + l) * 4;
                        int tf = ((dBlockY + y) * W + dBlockX + l) * 4;
                        d[tf] = tmp[sf];
                        d[tf + 1] = tmp[sf + 1];
                        d[tf + 2] = tmp[sf + 2];
                    }
                // 写原索引到块首 rbit 像素 R LSB
                for (int b = 0; b < rbit; b++)
                {
                    int f = ((dBlockY) * W + dBlockX + b) * 4;
                    d[f] = (byte)((d[f] & 254) | ((src >> b) & 1));
                }
            }
        }
        else
        {
            var idx = new int[bl];
            var tmp = (byte[])d.Clone();
            for (int j = 0; j < bl; j++)
            {
                int bX = x0 + (j % bx) * U, bY = y0 + (j / bx) * U;
                int v = 0;
                for (int b = 0; b < rbit; b++)
                {
                    int f = (bY * W + bX + b) * 4;
                    v |= (tmp[f] & 1) << b;
                }
                idx[j] = v;
            }
            for (int j = 0; j < bl; j++)
            {
                int dst = idx[j];
                if (dst < 0 || dst >= bl) continue;
                int sBlockX = x0 + (j % bx) * U, sBlockY = y0 + (j / bx) * U;
                int dBlockX = x0 + (dst % bx) * U, dBlockY = y0 + (dst / bx) * U;
                for (int y = 0; y < U; y++)
                    for (int l = 0; l < U; l++)
                    {
                        int sf = ((sBlockY + y) * W + sBlockX + l) * 4;
                        int tf = ((dBlockY + y) * W + dBlockX + l) * 4;
                        d[tf] = tmp[sf];
                        d[tf + 1] = tmp[sf + 1];
                        d[tf + 2] = tmp[sf + 2];
                    }
            }
        }
    }

    /// <summary>多区域：加密正序、解密倒序（与网页端一致）。</summary>
    public static int ApplyRegions(Rgba img, IReadOnlyList<(int x, int y, int w, int h)> regions,
                                   string password, int dx, int dy, bool encode, bool sort, bool soft, int unit)
    {
        int done = 0;
        int n = regions.Count;
        if (encode)
        {
            for (int i = 0; i < n; i++)
            {
                var r = regions[i];
                if (Apply(img, r.x + dx, r.y + dy, r.w, r.h, CxHash.RegionSeed(password, i), true, sort, soft, unit))
                    done++;
            }
        }
        else
        {
            for (int i = n - 1; i >= 0; i--)
            {
                var r = regions[i];
                if (Apply(img, r.x + dx, r.y + dy, r.w, r.h, CxHash.RegionSeed(password, i), false, sort, soft, unit))
                    done++;
            }
        }
        return done;
    }
}
