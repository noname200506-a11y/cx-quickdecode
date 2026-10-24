// 快捷键文本 ↔ 修饰键/键码（设置界面录制 + 注册用）
using System.Windows.Forms;

namespace CxQuickDecode.App;

internal static class HotkeyText
{
    public static bool TryParse(string? text, out uint mods, out Keys key)
    {
        mods = 0;
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            bool last = i == parts.Length - 1;
            switch (p.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": mods |= Native.MOD_CONTROL; break;
                case "ALT": mods |= Native.MOD_ALT; break;
                case "SHIFT": mods |= Native.MOD_SHIFT; break;
                case "WIN": return false;                 // Win 键不参与（避免和系统热键打架）
                default:
                    if (!last) return false;
                    if (!Enum.TryParse(p, true, out key) || key == Keys.None) return false;
                    break;
            }
        }
        return key != Keys.None && mods != 0;
    }

    public static string Format(uint mods, Keys key)
    {
        var list = new List<string>();
        if ((mods & Native.MOD_CONTROL) != 0) list.Add("Ctrl");
        if ((mods & Native.MOD_ALT) != 0) list.Add("Alt");
        if ((mods & Native.MOD_SHIFT) != 0) list.Add("Shift");
        list.Add(KeyName(key));
        return string.Join("+", list);
    }

    private static string KeyName(Keys key)
    {
        if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num" + (key - Keys.NumPad0);
        return key.ToString();
    }

    /// <summary>从一次按键事件里生成组合（必须带至少一个修饰键，且不是纯修饰键）。</summary>
    public static bool Capture(Keys keyData, out uint mods, out Keys key)
    {
        mods = 0;
        key = keyData & Keys.KeyCode;
        var m = keyData & Keys.Modifiers;
        if ((m & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
        if ((m & Keys.Alt) != 0) mods |= Native.MOD_ALT;
        if ((m & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
        bool isModifier = key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin;
        return !isModifier && key != Keys.None && mods != 0;
    }
}
