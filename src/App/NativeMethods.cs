// Win32 互操作：全局热键、工作集收缩、BitBlt 抓屏、窗口拖动、DPI
using System.Runtime.InteropServices;

namespace CxQuickDecode.App;

internal static class Native
{
    // ---- 窗口查找 / 激活（单实例时把已有窗口拉到前面）
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    public const int SW_SHOW = 5;

    /// <summary>找本程序的主悬浮窗（WinForms 窗口类名以 WindowsForms10.Window 开头，标题固定）。</summary>
    public static IntPtr FindAppWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            var cls = new System.Text.StringBuilder(128);
            GetClassName(h, cls, 128);
            if (!cls.ToString().StartsWith("WindowsForms10.Window", StringComparison.Ordinal)) return true;
            var title = new System.Text.StringBuilder(256);
            GetWindowText(h, title, 256);
            if (title.ToString() != "CX 快速解码") return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    // ---- 工作集 / 优先级
    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(int dwProcessId);

    // ---- 全局热键
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const int WM_HOTKEY = 0x0312;

    // ---- 窗口拖动（拖动标题条）
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HTCAPTION = 0x2;

    // ---- 剪贴板（原生读，绕开 OLE/GDI+ 转换：那会改掉最低位）
    [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterClipboardFormat(string lpszFormat);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClipboardFormatName(uint format, System.Text.StringBuilder lpszFormatName, int cchMaxCount);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GlobalSize(IntPtr hMem);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint DragQueryFile(IntPtr hDrop, uint iFile, System.Text.StringBuilder? lpszFile, uint cch);

    public const uint CF_BITMAP = 2;
    public const uint CF_DIB = 8;
    public const uint CF_HDROP = 15;
    public const uint CF_DIBV5 = 17;

    /// <summary>读剪贴板某格式的原始字节（GlobalLock 拷贝）。</summary>
    public static byte[]? ClipboardBytes(uint fmt)
    {
        IntPtr h = GetClipboardData(fmt);
        if (h == IntPtr.Zero) return null;
        IntPtr p = GlobalLock(h);
        if (p == IntPtr.Zero) return null;
        try
        {
            int size = (int)GlobalSize(h);
            if (size <= 0 || size > 512 * 1024 * 1024) return null;
            var buf = new byte[size];
            Marshal.Copy(p, buf, 0, size);
            return buf;
        }
        finally { GlobalUnlock(h); }
    }

    // ---- GDI 抓屏（BitBlt + GetDIBits，硬拿原始像素，不走 GDI+ 色彩转换 → 保住边框 LSB）
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h,
                                                             IntPtr hdcSrc, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines,
                                                               byte[] bits, ref BitmapInfo bmi, uint usage);

    public const uint SRCCOPY = 0x00CC0020;
    public const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
        // ByValArray 的 SizeConst 是「元素个数」；256 个 RGBQUAD(int) = 1024 字节，够 BI_RGB 用
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public int[] bmiColors;
    }

    /// <summary>抓指定屏幕区域（虚拟屏幕像素坐标）。</summary>
    public static byte[]? CaptureScreenBgra(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return null;
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hbm = CreateCompatibleBitmap(screenDc, w, h);
        if (memDc == IntPtr.Zero || hbm == IntPtr.Zero) return null;
        IntPtr old = SelectObject(memDc, hbm);
        try
        {
            if (!BitBlt(memDc, 0, 0, w, h, screenDc, x, y, SRCCOPY)) return null;
            var bmi = new BitmapInfo
            {
                biSize = 40,
                biWidth = w,
                biHeight = -h,          // 负 = 自上而下
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,      // BI_RGB
                biSizeImage = 0,
                bmiColors = new int[256]
            };
            var buf = new byte[w * h * 4];
            int lines = GetDIBits(memDc, hbm, 0, (uint)h, buf, ref bmi, DIB_RGB_COLORS);
            return lines == 0 ? null : buf;
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hbm);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
