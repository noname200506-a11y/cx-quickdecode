// IA.hash / IA.rng / IA.perm —— 与 cx-E2EE image-crypto.js 逐位等价
namespace CxQuickDecode.Core;

internal static class CxHash
{
    /// <summary>IA.hash(s)：h1+h2+h3+h4（JS Number 语义，下游只取 mod 2^32）。</summary>
    public static int Hash(string s)
    {
        int h1 = 1779033703, h2 = -1150833019 /*3144134277*/, h3 = 1013904242, h4 = -1521486534 /*2773480762*/;
        foreach (char c in s)                    // JS charCodeAt = UTF-16 code unit，C# char 同语义
        {
            int k = c;
            h1 = h2 ^ Js.Imul(h1 ^ k, 597399067);
            h2 = h3 ^ Js.Imul(h2 ^ k, -1425107063);
            h3 = h4 ^ Js.Imul(h3 ^ k, 951274213);
            h4 = h1 ^ Js.Imul(h4 ^ k, -1578923117);
        }
        return unchecked(h1 + h2 + h3 + h4);
    }

    public static int RegionSeed(string password, int index) => Hash(password + "#" + index.ToString());

    /// <summary>子串在 UTF-16 上的 hash（供签名用，JS 里就是 hash('V3|…')）。</summary>
    public static int Signature(string password, int count, IReadOnlyList<(int x, int y, int w, int h)> regions)
    {
        var sb = new System.Text.StringBuilder("V3|");
        sb.Append(count).Append('|');
        for (int i = 0; i < regions.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append(regions[i].x).Append(',').Append(regions[i].y).Append(',')
              .Append(regions[i].w).Append(',').Append(regions[i].h);
        }
        sb.Append('|').Append(password);
        return Hash(sb.ToString());
    }
}

/// <summary>IA.rng 的 xorshift32 变体（state 以 32 位模累加，等价于 JS 的 Number 累加 + ToInt32）。</summary>
internal struct XorShift32
{
    private int _state;

    public XorShift32(int seed) => _state = seed;

    public double Next()
    {
        unchecked { _state += 0x6D2B79F5; }
        int t = _state;
        t = Js.Imul(t ^ Js.Ushr(t, 15), t | 1);
        t ^= unchecked(t + Js.Imul(t ^ Js.Ushr(t, 7), t | 61));
        return (double)(uint)(t ^ Js.Ushr(t, 14)) / 4294967296.0;
    }
}

internal static class CxPerm
{
    /// <summary>IA.perm：Fisher-Yates。</summary>
    public static int[] Perm(int len, int seed)
    {
        var r = new XorShift32(seed);
        var p = new int[len];
        for (int i = 0; i < len; i++) p[i] = i;
        for (int i = len - 1; i > 0; i--)
        {
            int j = (int)Math.Floor(r.Next() * (i + 1));
            (p[i], p[j]) = (p[j], p[i]);
        }
        return p;
    }

    /// <summary>IA 的 cm 表：cm[i] = floor(nr() * 1e6)（Int32Array 语义）。</summary>
    public static int[] Cm(int len, int seed)
    {
        var nr = new XorShift32(unchecked(seed + 999));
        var cm = new int[len];
        for (int i = 0; i < len; i++) cm[i] = (int)Math.Floor(nr.Next() * 1e6);
        return cm;
    }
}
