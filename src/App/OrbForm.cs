// 悬浮球：圆形小窗
//   左键单击 = 剪贴板解码，左键拖动 = 移动（拖动时出现描边）
//   右键单击 = 截图解码，右键双击 = 退出球模式（回到悬浮窗）
//   拖图片进球 = 解码
//   外观可配：图标 / 文字 / 自定义图片（png·jpg·svg），各自可缩放旋转；背景色 + 背景不透明度
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CxQuickDecode.App;

internal sealed class OrbForm : Form
{
    private readonly FloatForm _owner;
    private readonly AppConfig _cfg;

    private bool _leftDown;
    private bool _moved;
    private bool _dragOutline;
    private Point _downScreen;
    private Point _formStart;

    private bool _hover;

    // 手势识别：单击延迟 320ms 判定（双击要能抢占，否则右键菜单会吃掉第二下）
    private readonly System.Windows.Forms.Timer _gesture = new() { Interval = 320 };
    private MouseButtons _pendBtn;
    private string _pendMods = "";
    private DateTime _lastDown = DateTime.MinValue;
    private ToolTip? _tip;
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 60 };

    // 内容位图缓存（按目标尺寸/缩放/旋转预渲染，避免每次重绘都缩放大图）
    private Bitmap? _content;
    private string _contentKey = "";

    public OrbForm(FloatForm owner, AppConfig cfg)
    {
        _owner = owner;
        _cfg = cfg;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = cfg.TopMost;
        BackColor = Color.FromArgb(30, 40, 60);
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        AllowDrop = true;
        AutoScaleMode = AutoScaleMode.None;
        // 钉死尺寸：无边框窗体有时会被撑宽（ToolTip/首次布局），用 Min=Max 锁住
        MinimumSize = new Size(cfg.OrbSize, cfg.OrbSize);
        MaximumSize = MinimumSize;
        Size = MinimumSize;

        _gesture.Tick += (_, _) =>
        {
            _gesture.Stop();
            if (_pendBtn == MouseButtons.None) return;
            if (_pendBtn == MouseButtons.Left && _moved) { _pendBtn = MouseButtons.None; return; }
            var btn = _pendBtn;
            var mods = _pendMods;
            _pendBtn = MouseButtons.None;
            RunAction(GestureText.Match(_cfg.OrbActions, btn, false, mods));
        };

        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += OnUp;

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                e.Effect = files != null && files.Any(ImageIo.IsImagePath) ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else e.Effect = DragDropEffects.None;
        };
        DragDrop += (_, e) =>
        {
            var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
            var f = files?.FirstOrDefault(ImageIo.IsImagePath);
            if (f != null) _owner.LoadAndDecode(f);
        };

        Load += (_, _) =>
        {
            Size = new Size(_cfg.OrbSize, _cfg.OrbSize);
            ApplyRegion();
            RestorePosition();
        };
        Resize += (_, _) => { ApplyRegion(); Invalidate(); };
        ApplyRegion();
    }

    public void ApplyLook()
    {
        var sz = new Size(_cfg.OrbSize, _cfg.OrbSize);
        MinimumSize = sz;
        MaximumSize = sz;
        Size = sz;
        Opacity = Math.Clamp(_cfg.OrbOpacity, 20, 100) / 100.0;
        TopMost = _cfg.TopMost;
        _contentKey = "";              // 强制重建内容位图
        ApplyRegion();
        Invalidate();
    }

    private void ApplyRegion()
    {
        using var p = new GraphicsPath();
        p.AddEllipse(0, 0, Width, Height);
        Region = new Region(p);
    }

    private void RestorePosition()
    {
        var v = SystemInformation.VirtualScreen;
        int x = _cfg.OrbX, y = _cfg.OrbY;
        bool ok = x != int.MinValue && y != int.MinValue &&
                  x > v.Left - Width && x < v.Right - 20 && y > v.Top - Height && y < v.Bottom - 20;
        if (!ok)
        {
            var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            x = wa.Right - Width - 40;
            y = wa.Bottom - Height - 140;
        }
        Location = new Point(x, y);
    }

    // ------------------------------------------------------------------ 鼠标
    private void OnDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _leftDown = true;
            _moved = false;
            _dragOutline = false;
            _downScreen = Cursor.Position;
            _formStart = Location;
        }

        var mods = GestureText.CurrentMods();
        var now = DateTime.UtcNow;

        // 第二下同键在 320ms 内 → 双击
        if (_pendBtn != MouseButtons.None && _pendBtn == e.Button
            && (now - _lastDown).TotalMilliseconds < 320)
        {
            _gesture.Stop();
            var btn = e.Button;
            var dblMods = _pendMods;
            _pendBtn = MouseButtons.None;
            _lastDown = DateTime.MinValue;
            RunAction(GestureText.Match(_cfg.OrbActions, btn, true, dblMods));
            return;
        }

        _lastDown = now;
        _pendBtn = e.Button;
        _pendMods = mods;
        _gesture.Stop();
        _gesture.Start();
    }

    /// <summary>按配置执行动作（找不到匹配就什么都不做）。</summary>
    private void RunAction(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        AppConfig.Log("orb action: " + id);
        switch (id)
        {
            case "clip": _owner.DoClipboard(); break;
            case "shot": _owner.DoShot(); break;
            case "menu": ShowTrayMenu(); break;
            case "exitOrb": _owner.ExitOrbMode(); break;
            case "gallery": _owner.OpenGallery(); break;
            case "settings": _owner.OpenSettings(); break;
        }
    }

    private void ShowTrayMenu()
    {
        var menu = _owner.TrayMenu;
        if (menu == null) return;
        menu.Show(this, new Point(Width + 6, Math.Max(0, Height / 4)));
    }

    private void OnMove(object? sender, MouseEventArgs e)
    {
        if (!_leftDown) return;
        var now = Cursor.Position;
        int dx = now.X - _downScreen.X, dy = now.Y - _downScreen.Y;
        if (!_moved && Math.Abs(dx) + Math.Abs(dy) > 4)
        {
            _moved = true;
            _dragOutline = true;        // 拖动时出现描边
            _pendBtn = MouseButtons.None;   // 拖动过就不算单击
            _gesture.Stop();
            Invalidate();
        }
        if (!_moved) return;
        Location = new Point(_formStart.X + dx, _formStart.Y + dy);
        _cfg.OrbX = Left;
        _cfg.OrbY = Top;
        _owner.RepositionPopup();       // 结果浮层跟随球，按新位置决定放上还是放下
    }

    private void OnUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _leftDown = false;
        bool wasMoved = _moved;
        _moved = false;
        if (_dragOutline) { _dragOutline = false; Invalidate(); }
        if (wasMoved) { _cfg.Save(); return; }
        _owner.RepositionPopup();         // 单击在 320ms 判定后由手势表决定
    }

    // ------------------------------------------------------------------ 绘制
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var r = ClientRectangle;
        r.Inflate(-1, -1);

        // 背景：可配颜色 + 独立不透明度（静默时不描边）
        int alpha = Math.Clamp(_cfg.OrbBgAlpha, 0, 255);
        Color rgb = Color.FromArgb(_cfg.OrbBgColor);
        using (var brush = new SolidBrush(Color.FromArgb(alpha, rgb)))
            g.FillEllipse(brush, r);

        // hover 时轻微提亮，便于确认点击目标（不描边）
        if (_hover && !_dragOutline && alpha > 30)
        {
            using var hi = new SolidBrush(Color.FromArgb(28, 255, 255, 255));
            g.FillEllipse(hi, r);
        }

        // 内容：图标 / 文字 / 自定义图片（各自缩放 + 旋转）
        var content = EnsureContent();
        if (content != null)
        {
            double scale = _cfg.OrbContent switch
            {
                "text" => _cfg.OrbTextScale,
                "image" => _cfg.OrbImageScale,
                _ => 1.0
            };
            int target = Math.Max(8, (int)Math.Round(Math.Min(Width, Height) * 0.62 * Math.Clamp(scale, 0.2, 1.6)));
            int angle = _cfg.OrbContent switch
            {
                "text" => _cfg.OrbTextRotate,
                "image" => _cfg.OrbImageRotate,
                _ => 0
            };
            var state = g.Save();
            g.TranslateTransform(Width / 2f, Height / 2f);
            if (angle != 0) g.RotateTransform(angle);
            g.DrawImage(content, new Rectangle(-target / 2, -target / 2, target, target));
            g.Restore(state);
        }

        // 拖动描边：只在拖拽时出现
        if (_dragOutline)
        {
            using var pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(2f, Width / 36f));
            var r2 = ClientRectangle;
            r2.Inflate(-2, -2);
            g.DrawEllipse(pen, r2);
            using var glow = new Pen(Color.FromArgb(90, 120, 190, 255), Math.Max(4f, Width / 16f));
            g.DrawEllipse(glow, r2);
        }
    }

    private Bitmap? EnsureContent()
    {
        int baseSize = Math.Max(16, (int)Math.Round(Math.Min(Width, Height) * 0.62));
        string key = $"{_cfg.OrbContent}|{_cfg.OrbText}|{_cfg.OrbImagePath}|{baseSize}|{_cfg.OrbTextScale}|{_cfg.OrbImageScale}";
        if (_content != null && key == _contentKey) return _content;
        _content?.Dispose();
        _content = BuildContent(baseSize);
        _contentKey = key;
        return _content;
    }

    private Bitmap? BuildContent(int px)
    {
        try
        {
            switch ((_cfg.OrbContent ?? "icon").ToLowerInvariant())
            {
                case "text":
                {
                    string text = string.IsNullOrWhiteSpace(_cfg.OrbText) ? "CX" : _cfg.OrbText;
                    var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(bmp);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    float size = px * 0.62f;
                    Font font = new("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
                    // 自适应：文字过宽就缩字号
                    for (int i = 0; i < 8; i++)
                    {
                        var sz = g.MeasureString(text, font);
                        if (sz.Width <= px * 0.94f) break;
                        font.Dispose();
                        size *= 0.88f;
                        font = new Font("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
                    }
                    using (font)
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var brush = new SolidBrush(Color.White))
                        g.DrawString(text, font, brush, new RectangleF(0, 0, px, px), sf);
                    return bmp;
                }
                case "image":
                {
                    if (string.IsNullOrWhiteSpace(_cfg.OrbImagePath) || !File.Exists(_cfg.OrbImagePath))
                        return IconFactory.AppIcon.ToBitmap();
                    return LoadAny(_cfg.OrbImagePath, px) ?? IconFactory.AppIcon.ToBitmap();
                }
                default:
                    return IconFactory.AppIcon.ToBitmap();
            }
        }
        catch (Exception ex)
        {
            AppConfig.Log("orb content failed: " + ex.Message);
            try { return IconFactory.AppIcon.ToBitmap(); } catch { return null; }
        }
    }

    /// <summary>加载 png / jpg / bmp / gif / svg（svg 用矢量按目标尺寸栅格化，缩小不会锯齿）。</summary>
    internal static Bitmap? LoadAny(string path, int targetPx)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".svg")
            {
                var doc = Svg.SvgDocument.Open(path);
                double bw = doc.ViewBox.Width > 0 ? doc.ViewBox.Width : Math.Max(1, doc.Width.Value);
                double bh = doc.ViewBox.Height > 0 ? doc.ViewBox.Height : Math.Max(1, doc.Height.Value);
                if (bw <= 0 || bh <= 0) { bw = bh = 1; }
                double s = targetPx / Math.Max(bw, bh);
                int w = Math.Max(1, (int)Math.Round(bw * s));
                int h = Math.Max(1, (int)Math.Round(bh * s));
                using var bmp = doc.Draw(w, h);
                // 居中到正方形画布
                var outBmp = new Bitmap(Math.Max(w, h), Math.Max(w, h), PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(outBmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(bmp, (outBmp.Width - w) / 2, (outBmp.Height - h) / 2, w, h);
                }
                return outBmp;
            }

            using var src = new Bitmap(path);
            int tw, th;
            double ratio = (double)src.Width / src.Height;
            if (ratio >= 1) { tw = targetPx; th = Math.Max(1, (int)Math.Round(targetPx / ratio)); }
            else { th = targetPx; tw = Math.Max(1, (int)Math.Round(targetPx * ratio)); }
            var canvas = new Bitmap(Math.Max(tw, th), Math.Max(tw, th), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using var attrs = new ImageAttributes();
                attrs.SetWrapMode(WrapMode.TileFlipXY);     // 缩小边缘不发黑/锯齿
                g.DrawImage(src, new Rectangle((canvas.Width - tw) / 2, (canvas.Height - th) / 2, tw, th),
                            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
            }
            return canvas;
        }
        catch (Exception ex)
        {
            AppConfig.Log($"load icon failed ({path}): {ex.Message}");
            return null;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _tip ??= new ToolTip { AutoPopDelay = 4000, InitialDelay = 600, ReshowDelay = 200 };
        _tip.SetToolTip(this, "左键：剪贴板解码\r\n右键：截图解码\r\n右键双击：回到悬浮窗\r\n可拖入图片");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip?.Dispose();
            _hoverTimer.Dispose();
            _gesture.Dispose();
            _content?.Dispose();
        }
        base.Dispose(disposing);
    }
}
