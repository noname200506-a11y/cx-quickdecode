// 悬浮球操作映射：修饰键 + 鼠标动作 → 一个动作（仿 Snipaste 的「贴图操作」表）
using System.Windows.Forms;

namespace CxQuickDecode.App;

internal sealed class OrbAction
{
    /// <summary>内部标识：clip / shot / menu / exitOrb / gallery / settings</summary>
    public string Id = "";
    /// <summary>显示名（不参与匹配）</summary>
    public string Name = "";
    public string Mod1 = "无";      // 无 / Ctrl / Shift / Alt
    public string Mod2 = "无";
    public string Gesture = "左键单击";   // 左键单击/左键双击/右键单击/右键双击/中键单击/中键双击

    public static List<OrbAction> Defaults() => new()
    {
        new() { Id = "clip",     Name = "剪贴板解码",     Mod1 = "无",   Mod2 = "无", Gesture = "左键单击" },
        new() { Id = "shot",     Name = "截图解码",       Mod1 = "Ctrl", Mod2 = "无", Gesture = "右键单击" },
        new() { Id = "menu",     Name = "打开托盘菜单",   Mod1 = "无",   Mod2 = "无", Gesture = "右键单击" },
        new() { Id = "exitOrb",  Name = "退出悬浮球模式", Mod1 = "无",   Mod2 = "无", Gesture = "右键双击" },
        new() { Id = "gallery",  Name = "打开图库",       Mod1 = "Shift",Mod2 = "无", Gesture = "左键单击" },
        new() { Id = "settings", Name = "打开设置",       Mod1 = "无",   Mod2 = "无", Gesture = "中键单击" },
    };

    public static readonly string[] ModOptions = { "无", "Ctrl", "Shift", "Alt" };
    public static readonly string[] Gestures =
    {
        "左键单击", "左键双击", "右键单击", "右键双击", "中键单击", "中键双击"
    };

    /// <summary>把两个修饰键归一化成 "Ctrl+Shift+Alt" 这种顺序。</summary>
    public static string NormMods(string a, string b)
    {
        var list = new List<string>();
        foreach (var s in new[] { a, b })
        {
            if (string.IsNullOrWhiteSpace(s) || s == "无") continue;
            if (!list.Contains(s)) list.Add(s);
        }
        return string.Join("+", list.OrderBy(x => x switch { "Ctrl" => 0, "Shift" => 1, "Alt" => 2, _ => 3 }));
    }

    public string ModsText => NormMods(Mod1, Mod2);
}

internal static class GestureText
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// 读物理按键状态（GetAsyncKeyState），不用 Control.ModifierKeys：
    /// 后者依赖焦点/消息队列，注入或窗口未激活时读不到。
    /// </summary>
    public static string CurrentMods()
    {
        // 两个来源取并集：物理按键状态（无焦点也读得到）+ 当前窗体修饰键（有焦点时最准）
        var m = Control.ModifierKeys;
        bool ctrl = Down(VK_CONTROL) || (m & Keys.Control) != 0;
        bool shift = Down(VK_SHIFT) || (m & Keys.Shift) != 0;
        bool alt = Down(VK_MENU) || (m & Keys.Alt) != 0;

        var list = new List<string>();
        if (ctrl) list.Add("Ctrl");
        if (shift) list.Add("Shift");
        if (alt) list.Add("Alt");
        return string.Join("+", list);
    }

    public static string BtnName(MouseButtons btn) => btn switch
    {
        MouseButtons.Left => "左键",
        MouseButtons.Right => "右键",
        MouseButtons.Middle => "中键",
        _ => "?"
    };

    /// <summary>按配置顺序匹配（由上至下优先）。</summary>
    public static string? Match(IReadOnlyList<OrbAction> actions, MouseButtons btn, bool isDouble, string mods)
    {
        string gesture = BtnName(btn) + (isDouble ? "双击" : "单击");
        foreach (var a in actions)
        {
            if (!string.Equals(a.Gesture, gesture, StringComparison.Ordinal)) continue;
            if (!string.Equals(a.ModsText, mods, StringComparison.Ordinal)) continue;
            return a.Id;
        }
        return null;
    }
}
