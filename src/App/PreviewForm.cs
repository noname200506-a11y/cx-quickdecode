// 结果预览：常见看图工具的操作逻辑
//   滚轮 = 以鼠标位置为锚点缩放；左键拖动 = 平移画面；双击 = 适应窗口 ⇄ 1:1
//   顶部有最大化 / 关闭，底部有 适应 / 1:1 / 缩小 / 放大 / 复制 / 另存为
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

/// <summary>自绘画布：负责缩放、平移、居中（缩放锚点跟随鼠标）。</summary>
internal sealed class ImageCanvas : Control
{
    private readonly Bitmap _bmp;
    private double _zoom = 1.0;
    private PointF _offset;
    private bool _fit = true;          // 处于"适应窗口"模式，窗口变化时自动重算
    private bool _dragging;
    private Point _dragStart;
    private PointF _offsetStart;

    public const double MinZoom = 0.05;
    public const double MaxZoom = 16.0;

    public event Action<double>? ZoomChanged;

    public double Zoom => _zoom;
    public bool IsFit => _fit;

    public ImageCanvas(Bitmap bmp)
    {
        _bmp = bmp;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(12, 15, 20);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    // ------------------------------------------------------------------ 缩放 / 定位
    public void FitToWindow()
    {
        if (Width <= 0 || Height <= 0) return;
        double z = Math.Min((double)Width / _bmp.Width, (double)Height / _bmp.Height);
        z = Math.Min(z, 1.0);                 // 适应窗口只缩不放，避免小图糊
        _zoom = Math.Clamp(z, MinZoom, MaxZoom);
        Center();
        _fit = true;
        ZoomChanged?.Invoke(_zoom);
        Invalidate();
    }

    public void SetActualSize()
    {
        _zoom = 1.0;
        Center();
        _fit = false;
        ZoomChanged?.Invoke(_zoom);
        Invalidate();
    }

    /// <summary>以 anchor（控件坐标）为不动点缩放。</summary>
    public void ZoomAt(Point anchor, double factor)
    {
        double target = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(target - _zoom) < 1e-9) return;

        // 锚点当前对应的图片坐标
        double ix = (anchor.X - _offset.X) / _zoom;
        double iy = (anchor.Y - _offset.Y) / _zoom;
        _zoom = target;
        _offset = new PointF(
            (float)(anchor.X - ix * _zoom),
            (float)(anchor.Y - iy * _zoom));
        _fit = false;
        ZoomChanged?.Invoke(_zoom);
        Invalidate();
    }

    public void ZoomCenter(double factor) => ZoomAt(new Point(Width / 2, Height / 2), factor);

    private void Center()
    {
        _offset = new PointF(
            (Width - _bmp.Width * (float)_zoom) / 2f,
            (Height - _bmp.Height * (float)_zoom) / 2f);
    }

    // ------------------------------------------------------------------ 鼠标
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.Location, e.Delta > 0 ? 1.15 : 1 / 1.15);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStart = e.Location;
        _offsetStart = _offset;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        _offset = new PointF(
            _offsetStart.X + (e.X - _dragStart.X),
            _offsetStart.Y + (e.Y - _dragStart.Y));
        _fit = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        if (_fit) SetActualSize();
        else FitToWindow();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_fit) FitToWindow();     // 适应模式跟着窗口变；1:1 / 手动缩放时保持
        else Invalidate();
    }

    // ------------------------------------------------------------------ 绘制
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // 缩小用高质量插值（避免锯齿），放大用最近邻（像素清晰，不糊）
        g.InterpolationMode = _zoom < 1.0 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = _zoom < 1.0 ? PixelOffsetMode.HighQuality : PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;

        var dest = new RectangleF(_offset.X, _offset.Y,
                                  (float)(_bmp.Width * _zoom), (float)(_bmp.Height * _zoom));

        // 只画可见部分（大图放大时省很多）
        var visible = RectangleF.Intersect(dest, ClientRectangle);
        if (visible.Width > 0 && visible.Height > 0)
        {
            float sx = (float)((visible.X - dest.X) / _zoom);
            float sy = (float)((visible.Y - dest.Y) / _zoom);
            float sw = (float)(visible.Width / _zoom);
            float sh = (float)(visible.Height / _zoom);

            // 源矩形夹在图片范围内，避免 GDI+ 越界（会抛/画黑边）
            sx = Math.Clamp(sx, 0, Math.Max(0, _bmp.Width - 1));
            sy = Math.Clamp(sy, 0, Math.Max(0, _bmp.Height - 1));
            sw = Math.Clamp(sw, 1, _bmp.Width - sx);
            sh = Math.Clamp(sh, 1, _bmp.Height - sy);

            g.DrawImage(_bmp, visible, new RectangleF(sx, sy, sw, sh), GraphicsUnit.Pixel);
        }

        // 图片边界（1:1 以上时帮助看对齐）
        if (_zoom >= 1.0)
        {
            using var pen = new Pen(Color.FromArgb(60, 120, 180), 1f);
            g.DrawRectangle(pen, dest.X, dest.Y, dest.Width, dest.Height);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}

internal sealed class PreviewForm : Form
{
    private readonly Rgba _img;
    private readonly Bitmap _bmp;
    private readonly string _info;
    private readonly ImageCanvas _canvas;
    private readonly Label _status;
    private readonly Button _btnMax;

    private bool _maximized;
    private Rectangle _restoreBounds;

    private Point _dragStart;
    private bool _dragging;

    private static readonly Color CBg = Color.FromArgb(20, 24, 31);
    private static readonly Color CBg2 = Color.FromArgb(30, 36, 46);
    private static readonly Color CTxt = Color.FromArgb(226, 233, 243);
    private static readonly Color CSub = Color.FromArgb(139, 152, 172);
    private static readonly Color CAccent = Color.FromArgb(47, 108, 235);

    public PreviewForm(Rgba img, string info)
    {
        _img = img;
        _bmp = ImageIo.ToBitmap(img);
        _info = info;

        Text = "CX 还原结果";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(900, 660);
        MinimumSize = new Size(420, 320);
        BackColor = CBg;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        // ---- 顶部条
        var top = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = CBg };
        var title = new Label
        {
            Text = "CX 还原结果",
            ForeColor = Color.FromArgb(200, 214, 232),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            Bounds = new Rectangle(14, 7, 260, 22),
            BackColor = Color.Transparent
        };
        _btnMax = TinyButton("", 0, 0, 28);
        _btnMax.Paint += PaintMaxIcon;
        var btnClose = TinyButton("✕", 0, 0, 28);
        _btnMax.Click += (_, _) => ToggleMaximize();
        btnClose.Click += (_, _) => Close();
        var topRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 76,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 3, 6, 0)
        };
        topRight.Controls.Add(btnClose);
        topRight.Controls.Add(_btnMax);
        top.Controls.Add(topRight);
        top.Controls.Add(title);
        foreach (Control c in new Control[] { top, title })
        {
            c.MouseDown += DragStart;
            c.MouseMove += DragMove;
            c.MouseUp += (_, _) => _dragging = false;
            c.DoubleClick += (_, _) => ToggleMaximize();
        }

        // ---- 画布
        _canvas = new ImageCanvas(_bmp) { Dock = DockStyle.Fill };
        _canvas.ZoomChanged += z => UpdateStatus(z);
        _canvas.MouseDown += DragStart;
        _canvas.MouseMove += DragMove;
        _canvas.MouseUp += (_, _) => _dragging = false;

        // ---- 底部工具条
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Color.FromArgb(26, 31, 40) };
        _status = new Label
        {
            Text = info,
            ForeColor = Color.FromArgb(150, 165, 186),
            AutoSize = false,
            Padding = new Padding(12, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };

        var bFit = ToolButton("适应", 0, 7);
        bFit.Click += (_, _) => _canvas.FitToWindow();
        var bOne = ToolButton("1:1", 0, 7);
        bOne.Click += (_, _) => _canvas.SetActualSize();
        var bOut = ToolButton("－", 0, 7, 34);
        bOut.Click += (_, _) => _canvas.ZoomCenter(1 / 1.15);
        var bIn = ToolButton("＋", 0, 7, 34);
        bIn.Click += (_, _) => _canvas.ZoomCenter(1.15);
        var bCopy = ToolButton("复制", 0, 7);
        bCopy.Click += (_, _) =>
        {
            ClipboardReader.PutImage(_img);
            _status.Text = "已复制到剪贴板";
        };
        var bSave = ToolButton("另存为…", 0, 7, 88, true);
        bSave.Click += (_, _) => SaveAs();

        var rightBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 430,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 9, 12, 0)
        };
        rightBar.Controls.Add(bSave);
        rightBar.Controls.Add(bCopy);
        rightBar.Controls.Add(bIn);
        rightBar.Controls.Add(bOut);
        rightBar.Controls.Add(bOne);
        rightBar.Controls.Add(bFit);

        _status.Dock = DockStyle.Fill;
        bottom.Controls.Add(_status);
        bottom.Controls.Add(rightBar);

        Controls.AddRange(new Control[] { _canvas, bottom, top });

        MouseDown += DragStart;
        MouseMove += DragMove;
        MouseUp += (_, _) => _dragging = false;

        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Escape: Close(); break;
                case Keys.F11: ToggleMaximize(); break;
                case Keys.Home: _canvas.FitToWindow(); break;
                case Keys.D0 when e.Control: _canvas.SetActualSize(); break;
                case Keys.Oemplus: case Keys.Add: _canvas.ZoomCenter(1.15); break;
                case Keys.OemMinus: case Keys.Subtract: _canvas.ZoomCenter(1 / 1.15); break;
                case Keys.S when e.Control: SaveAs(); break;
            }
        };

        Shown += (_, _) =>
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (Width > wa.Width - 40 || Height > wa.Height - 40)
            {
                Width = Math.Min(Width, wa.Width - 40);
                Height = Math.Min(Height, wa.Height - 40);
            }
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
            _canvas.FitToWindow();
            UpdateStatus(_canvas.Zoom);
        };
        Resize += (_, _) => { if (!_maximized) ApplyRegion(); };
        ApplyRegion();
    }

    // ------------------------------------------------------------------ 控件工厂
    /// <summary>最大化 / 还原图标自绘（不依赖字体里的方框字形）。</summary>
    private void PaintMaxIcon(object? sender, PaintEventArgs e)
    {
        var b = (Button?)sender;
        if (b == null) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(b.ForeColor, 1.4f);
        int cx = b.ClientSize.Width / 2, cy = b.ClientSize.Height / 2;
        var r = new Rectangle(cx - 5, cy - 4, 10, 8);
        if (!_maximized)
        {
            g.DrawRectangle(pen, r);
        }
        else
        {
            // 还原：两个叠起来的小方框
            g.DrawRectangle(pen, r.X + 2, r.Y, r.Width - 2, r.Height - 2);
            g.DrawRectangle(pen, r.X, r.Y + 2, r.Width - 2, r.Height - 2);
        }
    }

    private Button TinyButton(string text, int x, int y, int w)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, w, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = CBg,
            ForeColor = CSub,
            Font = new Font("Segoe UI", 9.5f),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.MouseEnter += (_, _) => { b.BackColor = CBg2; b.ForeColor = CTxt; };
        b.MouseLeave += (_, _) => { b.BackColor = CBg; b.ForeColor = CSub; };
        return b;
    }

    private Button ToolButton(string text, int x, int y, int w = 56, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, w, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? CAccent : CBg2,
            ForeColor = primary ? Color.White : CTxt,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private void UpdateStatus(double zoom)
    {
        _status.Text = $"{_img.W}×{_img.H} · {zoom * 100:0}%  ·  {_info}";
    }

    // ------------------------------------------------------------------ 最大化 / 拖动
    private void ToggleMaximize()
    {
        if (!_maximized)
        {
            _restoreBounds = Bounds;
            _maximized = true;
            _btnMax.Invalidate();
            MaximizedBounds = Screen.FromPoint(Cursor.Position).WorkingArea;
            WindowState = FormWindowState.Maximized;
            Region = null;                       // 最大化时不要圆角
        }
        else
        {
            _maximized = false;
            _btnMax.Invalidate();
            WindowState = FormWindowState.Normal;
            Bounds = _restoreBounds;
            ApplyRegion();
        }
        _canvas.Focus();
    }

    private void DragStart(object? sender, MouseEventArgs e)
    {
        if (_maximized || e.Button != MouseButtons.Left) return;
        // 点在画布上且图比视口大 → 交给画布平移；否则拖窗口
        if (sender == _canvas) return;
        _dragging = true;
        _dragStart = e.Location;
    }

    private void DragMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left) return;
        Left += e.X - _dragStart.X;
        Top += e.Y - _dragStart.Y;
    }

    // ------------------------------------------------------------------ 外观
    private void ApplyRegion()
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

    private void SaveAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png",
            FileName = ImageIo.SuggestedName("CX解码"),
            Title = "保存还原结果"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, PngCodec.Encode(_img));
            _status.Text = "已保存：" + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex) { MessageBox.Show(this, "保存失败：" + ex.Message, "CX 快速解码"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bmp.Dispose();
        base.Dispose(disposing);
    }
}
