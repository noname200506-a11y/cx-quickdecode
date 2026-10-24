// 配置：默认存到「文档\CxQuickDecode\cxq.config.json」（便携：exe 旁放 cxq.portable 标记文件）
using System.Text;
using System.Text.Json;

namespace CxQuickDecode.App;

internal sealed class AppConfig
{
    // ---- 密码 / 解码
    public string Password = "";
    public bool SavePassword = true;
    public bool AutoCopy = true;
    public int ManualUnit = 0;          // 0=自动，8/16
    public int ManualSoft = -1;         // -1 自动
    public bool SearchOffset = true;

    // ---- 结果浮层
    public bool ShowPopup = true;
    public double PopupSeconds = 3.5;

    // ---- 窗口 / 悬浮球
    public string Mode = "window";      // window | orb
    public int LastX = int.MinValue;
    public int LastY = int.MinValue;
    public int OrbX = int.MinValue;
    public int OrbY = int.MinValue;
    public int OrbSize = 72;
    public int OrbOpacity = 100;        // 40..100（整个球的不透明度）
    public string OrbStyle = "icon";    // 旧字段（兼容）：icon | text
    public string OrbContent = "icon";  // icon | text | image
    public string OrbText = "CX";
    public double OrbTextScale = 1.0;   // 0.4..1.2
    public int OrbTextRotate = 0;       // -180..180
    public string OrbImagePath = "";
    public double OrbImageScale = 1.0;  // 0.3..1.2
    public int OrbImageRotate = 0;      // -180..180
    public int OrbBgColor = unchecked((int)0xFF3A74EE);   // 背景色（RGB）
    public int OrbBgAlpha = 255;        // 背景不透明度 0..255

    // ---- 行为
    public bool TopMost = true;
    public bool LowPower = true;
    public int IdleSeconds = 20;

    // ---- 快捷键（可改）
    public string HotkeyClip = "Ctrl+Alt+Q";
    public string HotkeyShot = "Ctrl+Alt+S";
    public string HotkeyFile = "Ctrl+Alt+O";

    public List<OrbAction> OrbActions = new();
    public List<string> RecentPasswords = new();

    // ------------------------------------------------------------------ 路径
    private static string? _dir;
    private static readonly byte[] XorKey = Encoding.UTF8.GetBytes("CxQuickDecode/2026");
    private static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true, WriteIndented = true };

    /// <summary>便携模式：exe 同目录有 cxq.portable 标记文件。</summary>
    public static bool Portable => File.Exists(Path.Combine(AppContext.BaseDirectory, "cxq.portable"));

    public static string ConfigDir
    {
        get
        {
            if (_dir != null) return _dir;
            string dir = Portable
                ? AppContext.BaseDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CxQuickDecode");
            try { Directory.CreateDirectory(dir); } catch { }
            _dir = dir;
            return dir;
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDir, "cxq.config.json");

    private static string LegacyPath => Path.Combine(AppContext.BaseDirectory, "cxq.config.json");

    // ------------------------------------------------------------------ 读写
    public static AppConfig Load()
    {
        try
        {
            // 从旧位置（exe 同目录）迁移一次：用户之前填的密码不至于丢
            if (!File.Exists(ConfigPath) && !Portable && File.Exists(LegacyPath))
            {
                try
                {
                    string text = File.ReadAllText(LegacyPath);
                    if (text.Trim().Length > 2)
                    {
                        Directory.CreateDirectory(ConfigDir);
                        File.WriteAllText(ConfigPath, text);
                        File.Move(LegacyPath, LegacyPath + ".bak", true);
                    }
                }
                catch { }
            }

            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts)
                          ?? new AppConfig();
                cfg.Password = Unscramble(cfg.Password);
                if (cfg.OrbActions == null || cfg.OrbActions.Count == 0)
                    cfg.OrbActions = OrbAction.Defaults();
                // 旧配置兼容：OrbStyle=text → OrbContent=text
                if (string.Equals(cfg.OrbStyle, "text", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(cfg.OrbContent, "icon", StringComparison.OrdinalIgnoreCase))
                    cfg.OrbContent = "text";
                return cfg;
            }
        }
        catch (Exception ex) { Log("config load failed: " + ex.Message); }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            var clone = (AppConfig)MemberwiseClone();
            clone.Password = SavePassword ? Scramble(Password) : "";
            clone.RecentPasswords = SavePassword ? RecentPasswords : new List<string>();
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(clone, JsonOpts));
        }
        catch (Exception ex) { Log("config save failed: " + ex.Message); }
    }

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(Path.Combine(ConfigDir, "cxq.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
        }
        catch { }
    }

    // 轻混淆（防肩窥，不是加密）
    private static string Scramble(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = Encoding.UTF8.GetBytes(s);
        for (int i = 0; i < b.Length; i++) b[i] ^= XorKey[i % XorKey.Length];
        return "b64:" + Convert.ToBase64String(b);
    }

    private static string Unscramble(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try
        {
            if (!s.StartsWith("b64:")) return s;
            var b = Convert.FromBase64String(s.Substring(4));
            for (int i = 0; i < b.Length; i++) b[i] ^= XorKey[i % XorKey.Length];
            return Encoding.UTF8.GetString(b);
        }
        catch { return ""; }
    }
}
