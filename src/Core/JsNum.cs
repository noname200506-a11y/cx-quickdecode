// JS 数值语义（ToInt32 / ToUint32 / Math.imul / >>> / ^）—— 与网页端 IA 算法逐位对齐的前提
namespace CxQuickDecode.Core;

internal static class Js
{
    public static int Imul(int a, int b) => unchecked((int)((uint)a * (uint)b));

    public static int Ushr(int a, int n) => unchecked((int)((uint)a >> n));

    public static uint U32(int a) => unchecked((uint)a);

    /// <summary>ToUint8Clamp：NaN→0，&lt;=0→0，&gt;=255→255，否则 round-half-to-even。</summary>
    public static byte ClampU8(double v)
    {
        if (double.IsNaN(v)) return 0;
        if (v <= 0.0) return 0;
        if (v >= 255.0) return 255;
        return (byte)Math.Round(v, MidpointRounding.ToEven);
    }
}
