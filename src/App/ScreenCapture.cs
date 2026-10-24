// 全屏抓取 + 框选覆盖层（抓的是 BitBlt 原始像素，1:1 不缩放 → 边框 LSB 还在）
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal sealed class ScreenShot
{
    public Rgba Full = null!;
    public Bitmap Bitmap = null!;
    public Rectangle VirtualBounds;
}

internal static class ScreenCapture
{
    public static ScreenShot Capture()
    {
        var vb = SystemInformation.VirtualScreen;
        var bgra = Native.CaptureScreenBgra(vb.X, vb.Y, vb.Width, vb.Height);
        if (bgra == null) throw new InvalidOperationException("抓屏失败（BitBlt 返回空）");
        var img = ImageIo.FromBgra(bgra, vb.Width, vb.Height);
        return new ScreenShot { Full = img, Bitmap = ImageIo.ToBitmap(img), VirtualBounds = vb };
    }
}

/// <summary>全屏框选：显示静态截图 + 半透明遮罩，拖出矩形即选区。</summary>
internal sealed class ShotOverlay : Form
{
    private readonly ScreenShot _shot;
    private Point _start;
    private Point _end;
    private bool _dragging;
    private readonly Label _hint;

    public Rectangle SelectionScreen { get; private set; }   // 屏幕虚拟坐标
    public bool Canceled { get; private set; } = true;

    public ShotOverlay(ScreenShot shot)
    {
        _shot = shot;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;     // 全屏覆盖层用物理像素，绝不能让 WinForms 再缩放一次
        Bounds = shot.VirtualBounds;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        BackColor = Color.Black;
        Text = "框选打码区域（右键 / Esc 取消）";

        _hint = new Label
        {
            AutoSize = false,
            Text = "拖出要还原的区域 · Esc 取消",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(160, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 10f),
            Height = 34
        };
        Controls.Add(_hint);
        RepositionHint();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _shot.VirtualBounds;          // 再钉一次，防高 DPI 下被 AutoScale 改掉
        Program.Dbg($"overlay shown bounds={Bounds} screen={_shot.VirtualBounds}");
        Activate();
        Focus();
    }

    private void RepositionHint()
    {
        _hint.Width = 320;
        _hint.Left = (Width - _hint.Width) / 2;
        _hint.Top = 28;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImageUnscaled(_shot.Bitmap, 0, 0);

        var sel = CurrentRect();
        using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            if (sel.Width <= 0 || sel.Height <= 0)
            {
                g.FillRectangle(dim, 0, 0, Width, Height);
            }
            else
            {
                // 只暗化选区之外
                g.FillRectangle(dim, 0, 0, Width, sel.Top);
                g.FillRectangle(dim, 0, sel.Bottom, Width, Height - sel.Bottom);
                g.FillRectangle(dim, 0, sel.Top, sel.Left, sel.Height);
                g.FillRectangle(dim, sel.Right, sel.Top, Width - sel.Right, sel.Height);

                using var pen = new Pen(Color.FromArgb(255, 80, 160, 255), 2f);
                g.DrawRectangle(pen, sel);
                string label = $"{sel.Width} × {sel.Height}";
                using var f = new Font("Consolas", 9f);
                var sz = g.MeasureString(label, f);
                var pos = new PointF(sel.Left + 6, Math.Max(0, sel.Top - sz.Height - 4));
                using var bg = new SolidBrush(Color.FromArgb(200, 20, 24, 32));
                g.FillRectangle(bg, pos.X, pos.Y, sz.Width + 8, sz.Height + 2);
                g.DrawString(label, f, Brushes.White, pos.X + 4, pos.Y + 1);
            }
        }
        _hint.Visible = sel.Width <= 0 || sel.Height <= 0;
    }

    private Rectangle CurrentRect()
    {
        int x1 = Math.Min(_start.X, _end.X), y1 = Math.Min(_start.Y, _end.Y);
        int x2 = Math.Max(_start.X, _end.X), y2 = Math.Max(_start.Y, _end.Y);
        return Rectangle.FromLTRB(x1, y1, x2, y2);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { Canceled = true; Close(); return; }
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _start = _end = e.Location;
        Program.Dbg($"overlay mousedown {e.Location}");
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _end = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        _end = e.Location;
        var r = CurrentRect();
        if (r.Width < 8 || r.Height < 8) { Canceled = true; Close(); return; }
        SelectionScreen = new Rectangle(_shot.VirtualBounds.X + r.X, _shot.VirtualBounds.Y + r.Y, r.Width, r.Height);
        Program.Dbg($"overlay mouseup sel={SelectionScreen}");
        Canceled = false;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { Canceled = true; Close(); }
    }

    // 注意：不要用 OnDeactivate 自动取消 —— 覆盖层刚显示时的焦点抖动会把它瞬间关掉。
    // 取消只走 Esc / 右键 / 选区太小。
}
