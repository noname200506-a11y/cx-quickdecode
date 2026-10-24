using System.Windows.Forms;
using CxQuickDecode.App;
using CxQuickDecode.Core;

namespace CxQuickDecode;

internal static class Program
{
    internal static void Dbg(string msg)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "cxq.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
        }
        catch { }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a == "--selftest"))
        {
            Native.AttachConsole(-1);
            string logPath = Path.Combine(AppContext.BaseDirectory, "selftest.log");
            try
            {
                var fs = new StreamWriter(logPath) { AutoFlush = true };
                Console.SetOut(fs);
                Console.SetError(fs);
            }
            catch { }
            int i = Array.IndexOf(args, "--selftest");
            string dir = i + 1 < args.Length ? args[i + 1] : Path.Combine(AppContext.BaseDirectory, "vectors");
            try
            {
                int rc = SelfTest.Run(dir);
                Console.WriteLine(rc == 0 ? "SELFTEST PASS" : "SELFTEST FAIL");
                return rc;
            }
            catch (Exception ex)
            {
                Console.WriteLine("自检异常：" + ex);
                return 2;
            }
        }

        if (args.Any(a => a == "--actions"))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ActionsForm(AppConfig.Load()));
            return 0;
        }

        if (args.Any(a => a == "--settings"))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SettingsForm(AppConfig.Load()));
            return 0;
        }

        if (args.Any(a => a == "--decode" || a == "--decode-clipboard" || a == "--help"))
        {
            Native.AttachConsole(-1);
            if (args.Any(a => a == "--help"))
            {
                Console.WriteLine("CX 快速解码 —— 无界面用法");
                Console.WriteLine("  CxQuickDecode.exe --decode <图片> --password <密码> [--out <结果.png>]");
                Console.WriteLine("  CxQuickDecode.exe --decode-clipboard --password <密码> [--out <结果.png>]");
                Console.WriteLine("  CxQuickDecode.exe --decode <图片> --aabb 0,0,320,240 --unit 8 --soft 0");
                Console.WriteLine("  CxQuickDecode.exe --selftest <参考向量目录>");
                return 0;
            }
            return Cli.Run(args);
        }

        // 单实例：用 exe 同目录的独占文件锁（进程被强杀也会自动释放，比命名 Mutex 稳）
        FileStream? instanceLock = null;
        try
        {
            instanceLock = new FileStream(Path.Combine(AppContext.BaseDirectory, ".cxq.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch
        {
            // 拿不到锁：先看是不是已有窗口在跑，是就把它拉到前面，然后自己退出
            IntPtr existing = Native.FindAppWindow();
            if (existing != IntPtr.Zero)
            {
                Native.ShowWindow(existing, Native.SW_SHOW);
                Native.SetForegroundWindow(existing);
                return 0;
            }
            // 目录不可写之类：不做单实例限制，继续启动
        }
        _ = instanceLock;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) =>
            MessageBox.Show("出了点问题：" + e.Exception.Message, "CX 快速解码");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show("出了点问题：" + (e.ExceptionObject as Exception)?.Message, "CX 快速解码");

        var cfg = AppConfig.Load();
        Application.Run(new FloatForm(cfg));
        return 0;
    }
}
