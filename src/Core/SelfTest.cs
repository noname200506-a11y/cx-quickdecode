// 自检：与 Python(numpy) 参考实现逐字节对齐（tests/make_vectors.py 生成的向量）
using System.Text.Json;

namespace CxQuickDecode.Core;

internal static class SelfTest
{
    public static int Run(string vectorsDir)
    {
        int fails = 0, pass = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (ok) { pass++; Console.WriteLine("  [ok]   " + name); }
            else { fails++; Console.WriteLine("  [FAIL] " + name + "  " + detail); }
        }

        Console.WriteLine("== 1) hash / perm 基准值 ==");
        var cases = new[]
        {
            ("", 601251227),              // 由 Python 参考实现在运行时校验，这里只做自一致（见 vectors/hash.json）
            ("pw", 0),
            ("测试密码123", 0),
            ("pw#0", 0),
        };
        var hashJson = Path.Combine(vectorsDir, "hash.json");
        if (File.Exists(hashJson))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(hashJson));
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string s = el.GetProperty("s").GetString() ?? "";
                int expect = el.GetProperty("h").GetInt32();
                int got = CxHash.Hash(s);
                Check($"hash({Quote(s)})", got == expect, $"expect {expect} got {got}");
            }
        }
        else Console.WriteLine("  (跳过：没有 hash.json)");

        var permJson = Path.Combine(vectorsDir, "perm.json");
        if (File.Exists(permJson))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(permJson));
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                int seed = el.GetProperty("seed").GetInt32();
                int len = el.GetProperty("len").GetInt32();
                var expect = el.GetProperty("p").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var got = CxPerm.Perm(len, seed);
                Check($"perm(len={len},seed={seed})", got.SequenceEqual(expect),
                      "首差位置=" + FirstDiff(got, expect));
            }
        }
        else Console.WriteLine("  (跳过：没有 perm.json)");

        Console.WriteLine("== 2) PNG 编解码往返 ==");
        var pngDir = Path.Combine(vectorsDir, "png");
        if (Directory.Exists(pngDir))
        {
            foreach (var f in Directory.GetFiles(pngDir, "*.png").OrderBy(x => x))
            {
                try
                {
                    var img = PngCodec.Decode(File.ReadAllBytes(f));
                    var re = PngCodec.Encode(img);
                    var img2 = PngCodec.Decode(re);
                    bool same = img.Data.AsSpan().SequenceEqual(img2.Data);
                    Check("往返 " + Path.GetFileName(f) + $" {img.W}x{img.H}", same);
                }
                catch (Exception ex) { Check("往返 " + Path.GetFileName(f), false, ex.Message); }
            }
        }

        Console.WriteLine("== 3) 元数据解码（图 + 密码）vs Python 参考 ==");
        var caseDir = Path.Combine(vectorsDir, "cases");
        if (Directory.Exists(caseDir))
        {
            foreach (var json in Directory.GetFiles(caseDir, "*.json").OrderBy(x => x))
            {
                string name = Path.GetFileNameWithoutExtension(json);
                string imgPath = Path.Combine(caseDir, name + ".png");
                string expPath = Path.Combine(caseDir, name + ".expect.png");
                if (!File.Exists(imgPath) || !File.Exists(expPath)) { Check(name, false, "缺文件"); continue; }
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(json));
                    string pw = doc.RootElement.GetProperty("password").GetString() ?? "";
                    var src = PngCodec.Decode(File.ReadAllBytes(imgPath));
                    var expect = PngCodec.Decode(File.ReadAllBytes(expPath));
                    var outcome = Decoder.TryMeta(src, pw);
                    if (outcome == null) { Check(name + " 元数据识别", false, "没读到元数据"); continue; }
                    if (!outcome.Ok) { Check(name + " 解码", false, outcome.Message); continue; }
                    var got = outcome.Image!;
                    bool dimOk = got.W == expect.W && got.H == expect.H;
                    int maxd = dimOk ? MaxDiff(got, expect) : -1;
                    Check($"{name} {got.W}x{got.H} maxΔ={maxd}", dimOk && maxd == 0,
                          dimOk ? "" : $"尺寸 {got.W}x{got.H} vs {expect.W}x{expect.H}");
                }
                catch (Exception ex) { Check(name, false, ex.ToString()); }
            }
        }
        else Console.WriteLine("  (跳过：没有 cases)");

        Console.WriteLine("== 4) 手动区域路径（unit / soft 指定 + 自动择优）vs Python 参考 ==");
        var manualDir = Path.Combine(vectorsDir, "cases_manual");
        if (Directory.Exists(manualDir))
        {
            foreach (var json in Directory.GetFiles(manualDir, "m*.json").OrderBy(x => x))
            {
                string name = Path.GetFileNameWithoutExtension(json);
                string imgPath = Path.Combine(manualDir, name + ".png");
                string expPath = Path.Combine(manualDir, name + ".expect.png");
                if (!File.Exists(imgPath) || !File.Exists(expPath)) { Check(name, false, "缺文件"); continue; }
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(json));
                    string pw = doc.RootElement.GetProperty("password").GetString() ?? "";
                    int unit = doc.RootElement.GetProperty("unit").GetInt32();
                    bool soft = doc.RootElement.GetProperty("soft").GetBoolean();
                    var regions = new List<RectI>();
                    foreach (var r in doc.RootElement.GetProperty("regions").EnumerateArray())
                        regions.Add(new RectI(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32()));
                    var src = PngCodec.Decode(File.ReadAllBytes(imgPath));
                    var expect = PngCodec.Decode(File.ReadAllBytes(expPath));

                    // A) 指定参数：必须与参考逐字节一致
                    var a = Decoder.DecodeManual(src, new DecodeOptions
                    {
                        Password = pw, Regions = regions, Unit = unit,
                        SoftMode = soft ? 1 : 0, SearchOffset = false
                    });
                    int dA = a.Image == null ? -1 : MaxDiff(a.Image, expect);
                    Check($"{name} 指定 unit={unit} soft={soft} maxΔ={dA}", a.Ok && dA == 0, a.Message);

                    // B) 自动模式：自己挑 unit / soft / 偏移
                    var b = Decoder.DecodeManual(src, new DecodeOptions
                    {
                        Password = pw, Regions = regions, Unit = 0, SoftMode = -1, SearchOffset = true
                    });
                    int dB = b.Image == null ? -1 : MaxDiff(b.Image, expect);
                    Check($"{name} 自动择优 → unit={b.Unit} soft={(b.Soft ? 1 : 0)} maxΔ={dB}", b.Ok && dB == 0,
                          b.Message);
                }
                catch (Exception ex) { Check(name, false, ex.ToString()); }
            }
        }
        else Console.WriteLine("  (跳过：没有 cases_manual)");

        Console.WriteLine("== 5) sortRegion（色块排序 flag bit1）vs JS 语义参考 ==");
        var sortDir = Path.Combine(vectorsDir, "sort");
        if (Directory.Exists(sortDir))
        {
            foreach (var json in Directory.GetFiles(sortDir, "sort*.json").OrderBy(x => x))
            {
                string name = Path.GetFileNameWithoutExtension(json);
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(json));
                    int w = doc.RootElement.GetProperty("w").GetInt32();
                    int h = doc.RootElement.GetProperty("h").GetInt32();
                    int x0 = doc.RootElement.GetProperty("x0").GetInt32();
                    int y0 = doc.RootElement.GetProperty("y0").GetInt32();
                    int bx = doc.RootElement.GetProperty("bx").GetInt32();
                    int by = doc.RootElement.GetProperty("by").GetInt32();
                    int bl = doc.RootElement.GetProperty("bl").GetInt32();

                    var inp = PngCodec.Decode(File.ReadAllBytes(Path.Combine(sortDir, name + ".in.png")));
                    var enc = PngCodec.Decode(File.ReadAllBytes(Path.Combine(sortDir, name + ".enc.png")));
                    var dec = PngCodec.Decode(File.ReadAllBytes(Path.Combine(sortDir, name + ".dec.png")));

                    var a = inp.Clone();
                    RegionEnc.SortRegion(a, x0, y0, bx, by, bl, true);
                    int dEnc = MaxDiff(a, enc);
                    Check($"{name} 排序(enc) {w}x{h} bl={bl} maxΔ={dEnc}", dEnc == 0);

                    var b = enc.Clone();
                    RegionEnc.SortRegion(b, x0, y0, bx, by, bl, false);
                    int dDec = MaxDiff(b, dec);
                    Check($"{name} 逆排(dec) maxΔ={dDec}", dDec == 0);
                }
                catch (Exception ex) { Check(name, false, ex.Message); }
            }
        }
        else Console.WriteLine("  (跳过：没有 sort 向量)");

        Console.WriteLine($"\n结果：{pass} 通过 / {fails} 失败");
        return fails == 0 ? 0 : 1;
    }

    private static string Quote(string s) => "\"" + s.Replace("\n", "\\n") + "\"";

    private static int FirstDiff(int[] a, int[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return a.Length == b.Length ? -1 : n;
    }

    private static int MaxDiff(Rgba a, Rgba b)
    {
        if (a.W != b.W || a.H != b.H) return -1;
        int m = 0;
        for (int i = 0; i < a.Data.Length; i++)
        {
            int d = Math.Abs(a.Data[i] - b.Data[i]);
            if (d > m) m = d;
            if (m == 255) break;
        }
        return m;
    }
}
