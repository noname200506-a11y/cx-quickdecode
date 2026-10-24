// 结果浮层：贴在悬浮窗 / 悬浮球的上方或下方，显示几秒后淡出（不改变窗口尺寸、不抢焦点）
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal sealed class ResultPopup : Form
{
    private const int MaxW = 420;
    private const int MaxH = 320;
    private const int InfoH = 22;
    private const int Pad = 8;

    private readonly System.Windows.Forms.Timer _life = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer _fade = new() { Interval = 30 };
    private Bitmap? _bmp;
    private int _totalMs;
    private int _elapsedMs;
    private bool _hover;

    public event Action? OpenRequested;

    /// <summary>浮层被点击（供外部排查用）</summary>
    public bool WasClicked { get; private set; }

    public ResultPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(16, 20, 27);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;

        _life.Tick += (_, _) =>
        {
            if (_hover) return;
            _elapsedMs += _life.Interval;
            if (_elapsedMs >= _totalMs) { _life.Stop(); _fade.Start(); }
        };
        _fade.Tick += (_, _) =>
        {
            Opacity -= 0.09;
            if (Opacity <= 0.05) { _fade.Stop(); HideNow(); }
        };
        MouseEnter += (_, _) =>
        {
            _hover = true;
            _fade.Stop();
            Opacity = 1;
            _elapsedMs = 0;          // 停在浮层上就一直不消失
        };
        MouseLeave += (_, _) => { _hover = false; };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Activate();
        };
        MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) FireOpen();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000;    // WS_EX_NOACTIVATE
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    private string _info = "";
    private bool _openFired;

    /// <summary>点击浮层 = 打开完整预览（MouseUp 与 WndProc 双通道，防重入）。</summary>
    private void FireOpen()
    {
        if (_openFired) return;
        _openFired = true;
        AppConfig.Log("popup clicked");
        var handler = OpenRequested;
        HideNow();
        try { handler?.Invoke(); }
        catch (Exception ex) { AppConfig.Log("popup open handler failed: " + ex); }
    }

    protected override void WndProc(ref Message m)
    {
        // 兜底：某些情况下 WinForms 的 MouseUp 事件拿不到（无激活窗口），直接看窗口消息
        if (m.Msg == 0x0202)   // WM_LBUTTONUP
        {
            if (!_openFired) FireOpen();
        }
        base.WndProc(ref m);
    }
    private Rectangle _anchor;

    /// <summary>锚点变了（悬浮球被拖动）→ 立刻重新定位，并按新位置决定放上还是放下。</summary>
    public void Reposition(Rectangle anchor)
    {
        _anchor = anchor;
        if (!Visible) return;
        SetPosition();
    }

    private void SetPosition()
    {
        var wa = Screen.FromRectangle(_anchor).WorkingArea;
        int x = _anchor.Left + (_anchor.Width - Width) / 2;
        int yAbove = _anchor.Top - Height - 8;
        int yBelow = _anchor.Bottom + 8;
        int y;
        // 上面放得下就放上面；放不下就放下面；都放不下就贴着工作区顶
        if (yAbove >= wa.Top + 4) y = yAbove;
        else if (yBelow + Height <= wa.Bottom - 4) y = yBelow;
        else y = Math.Max(wa.Top + 4, yAbove);
        x = Math.Clamp(x, wa.Left + 4, Math.Max(wa.Left + 4, wa.Right - Width - 4));
        Location = new Point(x, y);
    }

    /// <summary>anchor = 悬浮窗 / 悬浮球的位置；优先贴在它上方，空间不够就下方。</summary>
    public void ShowResult(Rgba img, string info, Rectangle anchor, bool preferAbove, double seconds)
    {
        _info = info;
        double scale = Math.Min((double)MaxW / img.W, (double)MaxH / img.H);
        scale = Math.Min(scale, 1.0);                      // 小图不放大（避免糊）
        int dw = Math.Max(64, (int)Math.Round(img.W * scale));
        int dh = Math.Max(64, (int)Math.Round(img.H * scale));

        var old = _bmp;
        _bmp = ImageIo.ToBitmap(img);
        old?.Dispose();

        ClientSize = new Size(dw + Pad * 2, dh + InfoH + Pad * 2);
        RoundedRegion();

        _anchor = anchor;
        if (!preferAbove)
        {
            // 明确要求贴下方时，交换上下判断
            var wa = Screen.FromRectangle(anchor).WorkingArea;
            int xa = anchor.Left + (anchor.Width - Width) / 2;
            int yb = anchor.Bottom + 8;
            if (yb + Height > wa.Bottom - 4) yb = Math.Max(wa.Top + 4, anchor.Top - Height - 8);
            xa = Math.Clamp(xa, wa.Left + 4, Math.Max(wa.Left + 4, wa.Right - Width - 4));
            Location = new Point(xa, yb);
        }
        else SetPosition();

        Opacity = 1.0;
        _openFired = false;
        _elapsedMs = 0;
        _totalMs = (int)Math.Clamp(seconds * 1000, 800, 60000);
        Show();
        BringToFront();
        _fade.Stop();
        _life.Stop();
        _life.Start();
        Invalidate();
    }

    public void HideNow()
    {
        _life.Stop();
        _fade.Stop();
        Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (var bg = new SolidBrush(Color.FromArgb(16, 20, 27)))
            g.FillRectangle(bg, ClientRectangle);
        using (var border = new Pen(Color.FromArgb(80, 120, 200), 1f))
        {
            var r = ClientRectangle;
            r.Width -= 1; r.Height -= 1;
            g.DrawRectangle(border, r);
        }

        if (_bmp != null)
        {
            var dst = new Rectangle(Pad, Pad, ClientSize.Width - Pad * 2, ClientSize.Height - Pad * 2 - InfoH);
            g.DrawImage(_bmp, dst);
        }

        using var font = new Font("Microsoft YaHei UI", 8.5f);
        using var txt = new SolidBrush(Color.FromArgb(150, 168, 190));
        var infoRect = new Rectangle(Pad, ClientSize.Height - InfoH - Pad + 2, ClientSize.Width - Pad * 2, InfoH);
        g.DrawString(_info, font, txt, infoRect);

        // 右下角提示可点击（半透明，不抢信息）
        using var hintFont = new Font("Microsoft YaHei UI", 8f);
        using var hintBrush = new SolidBrush(Color.FromArgb(215, 150, 195, 255));
        using var sf = new StringFormat { Alignment = StringAlignment.Far };
        // 只在信息条右侧还有空位时画提示（图很小的时候不挤）
        if (ClientSize.Width > 300)
            g.DrawString("点击查看大图", hintFont, hintBrush,
                new RectangleF(Pad, ClientSize.Height - InfoH - Pad + 2, ClientSize.Width - Pad * 2, InfoH), sf);
    }

    private void RoundedRegion()
    {
        using var p = new GraphicsPath();
        int r = 12;
        var rect = ClientRectangle;
        p.AddArc(rect.Left, rect.Top, r, r, 180, 90);
        p.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
        p.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
        p.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
        p.CloseFigure();
        Region = new Region(p);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _bmp?.Dispose(); _life.Dispose(); _fade.Dispose(); }
        base.Dispose(disposing);
    }
}
