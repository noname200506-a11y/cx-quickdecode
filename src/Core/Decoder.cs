// 解码编排：① 元数据路径（图 + 密码，和网页端一样）② 无元数据兜底（截图/剪贴板位图：给区域 + 自动择优）
namespace CxQuickDecode.Core;

internal struct RectI
{
    public int X, Y, W, H;
    public RectI(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
    public override string ToString() => $"{X},{Y},{W},{H}";
}

internal sealed class DecodeOptions
{
    public string Password = "";
    /// <summary>手动区域；null = 整张图</summary>
    public List<RectI>? Regions;
    /// <summary>0 = 自动（8/16 择优），8 / 16 = 指定</summary>
    public int Unit;
    /// <summary>-1 自动，0 非 soft，1 soft</summary>
    public int SoftMode = -1;
    public bool SearchOffset = true;
    /// <summary>无元数据时，若图带边框，裁掉多少像素（0 = 不裁）</summary>
    public int Crop = 0;
    public int MaxSearchUnit = 16;
}

internal sealed class DecodeOutcome
{
    public bool Ok;
    public string Message = "";
    public Rgba? Image;
    public string Source = "";
    public MetaInfo? Meta;
    public int Unit;
    public bool Soft;
    public int Dx, Dy;
    public double Score;
    public long Ms;
    public bool PasswordWrong;
}

internal static class Decoder
{
    /// <summary>
    /// 主入口：先按网页端 V3 元数据路径解；读不到元数据时退回手动区域 + 自动择优。
    /// </summary>
    public static DecodeOutcome Decode(Rgba src, DecodeOptions opt)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var metaOutcome = TryMeta(src, opt.Password);
        if (metaOutcome != null)
        {
            metaOutcome.Ms = sw.ElapsedMilliseconds;
            return metaOutcome;
        }
        var manual = DecodeManual(src, opt);
        manual.Ms = sw.ElapsedMilliseconds;
        return manual;
    }

    /// <summary>元数据路径：返回 null = 图上没有可识别元数据（交给手动兜底）。</summary>
    public static DecodeOutcome? TryMeta(Rgba src, string password)
    {
        if (src.W <= Meta.BorderPng * 2 || src.H <= Meta.BorderPng * 2) return null;
        MetaInfo? info;
        try { info = Meta.ReadFromImage(src, Meta.BorderPng); }
        catch { return null; }
        if (info == null) return null;

        var o = new DecodeOutcome { Source = "meta", Meta = info, Unit = 8, Soft = info.Soft };
        if (!Meta.VerifyPassword(info, password))
        {
            o.Ok = false;
            o.PasswordWrong = true;
            o.Message = info.Decoy
                ? "密码不对：这张图使用诱饵模式，需要正确密码才能校验元数据签名"
                : "密码不对：元数据签名校验失败";
            return o;
        }

        var work = src.Clone();
        try
        {
            if (info.Decoy)
            {
                if (!Decoy.DecoyExtract(work, Meta.BorderPng, info.Regions, info.DataLen, info.DecoyScale))
                {
                    o.Ok = false;
                    o.Message = "诱饵数据提取失败（图片可能被缩放 / 重压）";
                    return o;
                }
                o.Score = 0;
                o.Message = $"诱饵模式：已按 LSB 取回区域原始像素（降采样 ×{info.DecoyScale}）";
            }
            else
            {
                RegionEnc.ApplyRegions(work, info.Regions, password, Meta.BorderPng, Meta.BorderPng,
                                       encode: false, sort: info.Sort, soft: info.Soft, unit: 8);
                o.Score = BlockSeamScore(work, info.Regions, 8, Meta.BorderPng, Meta.BorderPng);
                o.Message = $"元数据路径：{info.Count} 个区域"
                            + (info.Sort ? "，色块排序已逆排" : "")
                            + (info.Soft ? "，JPEG 亮度域" : "");
            }
            o.Image = work.Crop(Meta.BorderPng);
            o.Ok = true;
        }
        catch (Exception ex)
        {
            o.Ok = false;
            o.Message = "还原失败：" + ex.Message;
        }
        return o;
    }

    /// <summary>手动路径：给定区域（或整图）+ unit/soft/偏移，自动挑块缝最平的一组。</summary>
    public static DecodeOutcome DecodeManual(Rgba src, DecodeOptions opt)
    {
        var regions = opt.Regions is { Count: > 0 }
            ? opt.Regions
            : new List<RectI> { new RectI(0, 0, src.W, src.H) };

        var units = opt.Unit is 8 or 16 or 32 or 64 ? new[] { opt.Unit } : new[] { 8, 16 };
        var softs = opt.SoftMode switch
        {
            0 => new[] { false },
            1 => new[] { true },
            _ => new[] { false, true }
        };

        DecodeOutcome best = new() { Ok = false, Score = double.MaxValue, Source = "manual" };
        string? bestMsg = null;

        foreach (int unit in units)
        {
            foreach (bool soft in softs)
            {
                var cand = TryOne(src, regions, opt.Password, unit, soft, 0, 0, opt.Crop);
                if (cand != null && cand.Score < best.Score) { best = cand; }
            }
        }
        if (best.Image == null) best = TryOne(src, regions, opt.Password, units[0], false, 0, 0, opt.Crop)
                                       ?? new DecodeOutcome { Ok = false, Message = "区域太小，凑不出一个置换块" };

        // 偏移搜索：截图往往偏几个像素，块网格错位就全错。
        // 两阶段（先 x 再 y）+ 提前收敛，比全网格 unit² 快一个量级（848×1232 实测 7.3s → 0.6s）。
        if (opt.SearchOffset && best.Image != null && opt.Unit is not (8 or 16 or 32 or 64) && best.Score > 1.5)
        {
            int unit = best.Unit;
            bool soft = best.Soft;
            var bestCand = best;
            double bestScore = best.Score;

            for (int dx = 1; dx < unit; dx++)
            {
                var cand = TryOne(src, regions, opt.Password, unit, soft, dx, 0, opt.Crop);
                if (cand != null && cand.Score < bestScore - 1e-9) { bestScore = cand.Score; bestCand = cand; }
            }
            int bestDx = bestCand.Dx;
            for (int dy = 1; dy < unit; dy++)
            {
                var cand = TryOne(src, regions, opt.Password, unit, soft, bestDx, dy, opt.Crop);
                if (cand != null && cand.Score < bestScore - 1e-9) { bestScore = cand.Score; bestCand = cand; }
            }
            // 若已经"缝几乎消失"，不再精修
            if (bestScore > 1.5)
            {
                int bx0 = bestCand.Dx, by0 = bestCand.Dy;
                for (int dy = Math.Max(0, by0 - 1); dy <= Math.Min(unit - 1, by0 + 1); dy++)
                    for (int dx = Math.Max(0, bx0 - 1); dx <= Math.Min(unit - 1, bx0 + 1); dx++)
                    {
                        if (dx == bx0 && dy == by0) continue;
                        var cand = TryOne(src, regions, opt.Password, unit, soft, dx, dy, opt.Crop);
                        if (cand != null && cand.Score < bestScore - 1e-9) { bestScore = cand.Score; bestCand = cand; }
                    }
            }
            best = bestCand;
        }

        if (best.Image != null)
        {
            best.Message = (opt.Regions is { Count: > 0 } ? "按框选区域猜" : "按整图猜")
                           + $"（图上没有打码元数据）unit={best.Unit} soft={(best.Soft ? 1 : 0)} "
                           + $"偏移=({best.Dx},{best.Dy}) 块缝={best.Score:F1}";
            best.Meta = null;
            return best;
        }
        return new DecodeOutcome { Ok = false, Message = bestMsg ?? "无法还原：区域太小或参数不匹配" };
    }

    private static DecodeOutcome? TryOne(Rgba src, List<RectI> regions, string password,
                                         int unit, bool soft, int dx, int dy, int crop)
    {
        var work = src.Clone();
        try
        {
            var regionTuples = new List<(int x, int y, int w, int h)>(regions.Count);
            foreach (var r in regions) regionTuples.Add((r.X, r.Y, r.W, r.H));
            int done = RegionEnc.ApplyRegions(work, regionTuples, password, dx, dy, encode: false, sort: false, soft: soft, unit: unit);
            if (done == 0) return null;
            var scoreRegions = new List<(int x, int y, int w, int h)>(regions.Count);
            foreach (var r in regions) scoreRegions.Add((r.X, r.Y, r.W, r.H));
            double score = BlockSeamScore(work, scoreRegions, unit, dx, dy);
            var img = crop > 0 ? work.Crop(crop) : work;
            return new DecodeOutcome
            {
                Ok = true,
                Image = img,
                Source = "manual",
                Unit = unit,
                Soft = soft,
                Dx = dx,
                Dy = dy,
                Score = score
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 块缝能量：区域内每条 unit 网格边界的相邻像素差均值。
    /// 还原正确时块与块接缝消失 → 该值明显偏低；可用来在 unit/soft/偏移候选里挑正确的那组。
    /// </summary>
    public static double BlockSeamScore(Rgba img, IReadOnlyList<(int x, int y, int w, int h)> regions, int unit, int dx, int dy)
    {
        double sum = 0;
        long n = 0;
        var d = img.Data;
        int W = img.W, H = img.H;
        foreach (var r0 in regions)
        {
            int rx = r0.x + dx, ry = r0.y + dy;
            int x0 = (int)Math.Floor(rx / (double)unit) * unit;
            int y0 = (int)Math.Floor(ry / (double)unit) * unit;
            int x1 = Math.Min(W, (int)(Math.Ceiling((rx + r0.w) / (double)unit) * unit));
            int y1 = Math.Min(H, (int)(Math.Ceiling((ry + r0.h) / (double)unit) * unit));
            x1 = x0 + (int)Math.Floor((x1 - x0) / (double)unit) * unit;
            y1 = y0 + (int)Math.Floor((y1 - y0) / (double)unit) * unit;
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
            x1 = Math.Min(W, x1); y1 = Math.Min(H, y1);
            if (x1 - x0 < unit * 2 || y1 - y0 < unit * 2) continue;

            for (int x = x0 + unit; x < x1; x += unit)
            {
                for (int y = y0; y < y1; y++)
                {
                    int i0 = (y * W + x - 1) * 4, i1 = (y * W + x) * 4;
                    sum += Math.Abs(d[i0] - d[i1]) + Math.Abs(d[i0 + 1] - d[i1 + 1]) + Math.Abs(d[i0 + 2] - d[i1 + 2]);
                    n++;
                }
            }
            for (int y = y0 + unit; y < y1; y += unit)
            {
                for (int x = x0; x < x1; x++)
                {
                    int i0 = ((y - 1) * W + x) * 4, i1 = (y * W + x) * 4;
                    sum += Math.Abs(d[i0] - d[i1]) + Math.Abs(d[i0 + 1] - d[i1 + 1]) + Math.Abs(d[i0 + 2] - d[i1 + 2]);
                    n++;
                }
            }
        }
        return n == 0 ? double.MaxValue : sum / n;
    }
}
