// 无界面模式：给批处理 / 快捷方式 / 其它工具调用（也可用来做自动化验证）
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal static class Cli
{
    private static StreamWriter? _log;

    public static int Run(string[] args)
    {
        try { _log = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "cli.log"), false) { AutoFlush = true }; }
        catch { }

        string? file = GetArg(args, "--decode");
        bool clip = args.Any(a => a == "--decode-clipboard");
        string? outPath = GetArg(args, "--out");
        string password = GetArg(args, "--password") ?? "";
        string unitArg = GetArg(args, "--unit") ?? "0";
        string softArg = GetArg(args, "--soft") ?? "auto";
        string aabb = GetArg(args, "--aabb") ?? "";
        bool noSearch = args.Any(a => a == "--no-search-offset");

        Rgba img;
        string src;
        try
        {
            if (clip)
            {
                var ci = ClipboardReader.ReadFirstImage();
                if (ci.Image == null) { Log("FAIL 剪贴板：" + ci.Desc); return 3; }
                img = ci.Image;
                src = ci.Desc;
            }
            else if (!string.IsNullOrEmpty(file))
            {
                img = ImageIo.LoadFile(file);
                src = file;
            }
            else { Log("FAIL 需要 --decode <图片> 或 --decode-clipboard"); return 4; }
        }
        catch (Exception ex) { Log("FAIL 读图：" + ex.Message); return 3; }

        Log($"input: {src}  {img.W}x{img.H}");

        var regions = new List<RectI>();
        if (!string.IsNullOrWhiteSpace(aabb))
        {
            foreach (var part in aabb.Split(new[] { ';', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var nums = part.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => int.TryParse(s.Trim(), out int v) ? v : 0).ToArray();
                if (nums.Length >= 4) regions.Add(new RectI(nums[0], nums[1], nums[2], nums[3]));
            }
        }

        var opt = new DecodeOptions
        {
            Password = password,
            Regions = regions.Count > 0 ? regions : null,
            Unit = int.TryParse(unitArg, out int u) ? u : 0,
            SoftMode = softArg == "1" ? 1 : softArg == "0" ? 0 : -1,
            SearchOffset = !noSearch
        };

        DecodeOutcome r;
        try { r = Decoder.Decode(img, opt); }
        catch (Exception ex) { Log("FAIL 解码：" + ex); return 1; }

        Log($"ok={r.Ok} path={r.Source} unit={r.Unit} soft={(r.Soft ? 1 : 0)} dx={r.Dx} dy={r.Dy} " +
            $"seam={r.Score:F2} ms={r.Ms} msg={r.Message}");
        if (r.Ok && r.Image != null)
        {
            if (!string.IsNullOrEmpty(outPath))
            {
                File.WriteAllBytes(outPath, PngCodec.Encode(r.Image));
                Log("wrote " + outPath + $" ({r.Image.W}x{r.Image.H})");
            }
            return 0;
        }
        return 1;
    }

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        if (i < 0 || i + 1 >= args.Length) return null;
        return args[i + 1];
    }

    private static void Log(string s)
    {
        Console.WriteLine(s);
        try { _log?.WriteLine(s); } catch { }
    }
}
