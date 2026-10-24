// 桌面悬浮窗（固定尺寸，不随结果变化）：预输入密码 + 三种快捷解码
//   结果用「浮层」贴在窗口上/下方，几秒后自动消失
//   可缩略成悬浮球（左键剪贴板 / 右键截图 / 右键双击回窗口）
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal sealed class FloatForm : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int HK_CLIP = 0x0C01;
    private const int HK_SHOT = 0x0C02;
    private const int HK_FILE = 0x0C03;

    private const int W = 420;
    private const int H = 150;

    private readonly AppConfig _cfg;
    private readonly TextBox _pw;
    private readonly Button _btnClip, _btnShot, _btnFile;
    private readonly Label _title, _status;
    private readonly Button _btnOrb, _btnClose, _btnGallery, _btnSettings;
    private readonly System.Windows.Forms.Timer _idleTimer;
    private readonly ResultPopup _popup = new();

    private NotifyIcon? _tray;
    private GalleryForm? _gallery;
    private OrbForm? _orb;
    private readonly Dictionary<int, string> _hotkeyLabels = new();
    private DateTime _lastActive = DateTime.UtcNow;
    private Rgba? _lastResult;
    private Rgba? _lastInput;
    private string _lastInfo = "";
    private string _lastOutputSig = "";
    private string? _lastInputPath;
    private bool _busy;
    private bool _lowPower;
    private bool _trayHintShown;
    private bool _reallyExit;

    private static readonly Color CBg = Color.FromArgb(23, 27, 35);
    private static readonly Color CBg2 = Color.FromArgb(30, 36, 46);
    private static readonly Color CTxt = Color.FromArgb(226, 233, 243);
    private static readonly Color CSub = Color.FromArgb(139, 152, 172);
    private static readonly Color CAccent = Color.FromArgb(47, 108, 235);
    private static readonly Color COk = Color.FromArgb(94, 214, 143);
    private static readonly Color CBad = Color.FromArgb(245, 116, 116);
    private static readonly Color CWarn = Color.FromArgb(240, 190, 90);

    public FloatForm(AppConfig cfg)
    {
        _cfg = cfg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Text = "CX 快速解码";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = cfg.TopMost;
        ShowInTaskbar = false;
        BackColor = CBg;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(W, H);
        Font = new Font("Microsoft YaHei UI", 9f);
        AllowDrop = true;
        KeyPreview = true;
        Icon = IconFactory.AppIcon;

        // ---- 顶部条
        _title = new Label
        {
            Text = "CX 快速解码",
            ForeColor = CTxt,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            Bounds = new Rectangle(12, 6, 240, 20),
            BackColor = Color.Transparent
        };
        _btnOrb = SmallButton("◎", 356, 3);
        _btnClose = SmallButton("✕", 384, 3);
        _title.MouseDown += DragWindow;
        _btnOrb.Click += (_, _) => EnterOrbMode();
        _btnClose.Click += (_, _) => Hide();

        // ---- 密码
        var lblPw = new Label
        {
            Text = "密码",
            ForeColor = CSub,
            Bounds = new Rectangle(12, 41, 34, 20),
            BackColor = Color.Transparent
        };
        _pw = new TextBox
        {
            Bounds = new Rectangle(48, 37, 268, 26),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(15, 19, 25),
            ForeColor = CTxt,
            UseSystemPasswordChar = true,
            Text = cfg.Password
        };
        var btnEye = new Button
        {
            Text = "显示",
            Bounds = new Rectangle(322, 37, 86, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = CBg2,
            ForeColor = CSub,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Cursor = Cursors.Hand
        };
        btnEye.FlatAppearance.BorderSize = 0;
        bool visible = false;
        btnEye.Click += (_, _) =>
        {
            visible = !visible;
            _pw.UseSystemPasswordChar = !visible;
            btnEye.Text = visible ? "隐藏" : "显示";
            Wake();
        };
        _pw.TextChanged += (_, _) => { _cfg.Password = _pw.Text; Wake(); };
        _pw.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoClipboard(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Hide(); }
        };

        // ---- 三个按钮
        _btnClip = BigButton("剪贴板解码", 12, 72, true);
        _btnShot = BigButton("截图解码", 142, 72);
        _btnFile = BigButton("打开图片", 272, 72);
        _btnClip.Click += (_, _) => DoClipboard();
        _btnShot.Click += (_, _) => DoShot();
        _btnFile.Click += (_, _) => DoFile();

        // ---- 状态（单行，细节看浮层/预览）
        _status = new Label
        {
            Bounds = new Rectangle(12, 118, 292, 20),
            ForeColor = CSub,
            BackColor = Color.Transparent,
            AutoSize = false,
            AutoEllipsis = true,
            Text = "就绪 · 密码会自动记住 · 拖图片进来也行",
            Font = new Font("Microsoft YaHei UI", 8f)
        };
        _status.Click += (_, _) => ShowPreview();

        // ---- 悬浮栏上的入口按钮（图库 / 设置）
        _btnGallery = TinyTextButton("图库", 310, 117, 50);
        _btnSettings = TinyTextButton("设置", 364, 117, 50);
        _btnGallery.Click += (_, _) => OpenGallery();
        _btnSettings.Click += (_, _) => OpenSettings();

        Controls.AddRange(new Control[]
        {
            _title, _btnOrb, _btnClose, lblPw, _pw, btnEye, _btnClip, _btnShot, _btnFile,
            _status, _btnGallery, _btnSettings
        });

        _popup.OpenRequested += ShowPreview;

        // ---- 空闲降耗
        _idleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _idleTimer.Tick += (_, _) =>
        {
            if (!_cfg.LowPower) return;
            if ((DateTime.UtcNow - _lastActive).TotalSeconds >= Math.Max(3, _cfg.IdleSeconds)) EnterLowPower();
        };

        // ---- 拖放
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
            if (f != null) LoadAndDecode(f); else SetStatus("拖进来的是文件，但不是图片", CBad);
        };

        MouseEnter += (_, _) => Wake();
        MouseMove += (_, _) => Wake();
        MouseDown += OnAnyMouseDown;

        Load += (_, _) => RestorePosition();
        Move += (_, _) =>
        {
            if (Visible) { _cfg.LastX = Left; _cfg.LastY = Top; }
        };
        FormClosing += OnClosingForm;
        Shown += (_, _) =>
        {
            Wake();
            BuildTray();
            RegisterHotkeys();
            ApplyRegion();
            if (string.Equals(_cfg.Mode, "orb", StringComparison.OrdinalIgnoreCase))
            {
                Hide();
                EnterOrbMode(quiet: true);
            }
        };
        Resize += (_, _) => ApplyRegion();
        ApplyRegion();
    }

    // ------------------------------------------------------------------ 外观
    private Button SmallButton(string text, int x, int y)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, 24, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = CBg,
            ForeColor = CSub,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.MouseEnter += (_, _) => b.BackColor = CBg2;
        b.MouseLeave += (_, _) => b.BackColor = CBg;
        return b;
    }

    private Button BigButton(string text, int x, int y, bool primary = false)
    {
        var b = new Button
        {
            Bounds = new Rectangle(x, y, 124, 40),
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? CAccent : CBg2,
            ForeColor = primary ? Color.White : CTxt,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        if (!primary)
        {
            b.MouseEnter += (_, _) => b.BackColor = Color.FromArgb(40, 48, 62);
            b.MouseLeave += (_, _) => b.BackColor = CBg2;
        }
        else
        {
            b.MouseEnter += (_, _) => b.BackColor = Color.FromArgb(64, 124, 245);
            b.MouseLeave += (_, _) => b.BackColor = CAccent;
        }
        return b;
    }

    private Button TinyTextButton(string text, int x, int y, int w)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, w, 22),
            FlatStyle = FlatStyle.Flat,
            BackColor = CBg2,
            ForeColor = CSub,
            Font = new Font("Microsoft YaHei UI", 7.5f),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.MouseEnter += (_, _) => b.ForeColor = CTxt;
        b.MouseLeave += (_, _) => b.ForeColor = CSub;
        return b;
    }

    private void ApplyRegion()
    {
        using var p = new GraphicsPath();
        int r = 14;
        var rect = ClientRectangle;
        p.AddArc(rect.Left, rect.Top, r, r, 180, 90);
        p.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
        p.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
        p.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
        p.CloseFigure();
        Region = new Region(p);
    }

    private void RestorePosition()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        var v = SystemInformation.VirtualScreen;
        int x = _cfg.LastX, y = _cfg.LastY;
        bool onScreen = x != int.MinValue && y != int.MinValue &&
                        x > v.Left - Width + 60 && x < v.Right - 60 &&
                        y > v.Top - 10 && y < v.Bottom - 40;
        if (!onScreen)
        {
            x = wa.Right - Width - 28;
            y = wa.Bottom - Height - 40;
        }
        Location = new Point(x, y);
    }

    /// <summary>窗口上右键 = 弹托盘菜单（与悬浮球一致）；左键拖动窗口。</summary>
    private void OnAnyMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _tray?.ContextMenuStrip?.Show(this, e.Location);
            Wake();
            return;
        }
        DragWindow(sender, e);
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
        Wake();
    }

    // ------------------------------------------------------------------ 悬浮球
    private void EnterOrbMode(bool quiet = false)
    {
        if (_orb == null)
        {
            _orb = new OrbForm(this, _cfg);
            _orb.FormClosed += (_, _) => { _orb = null; };
        }
        _orb.ApplyLook();
        _orb.Show();
        _orb.BringToFront();
        if (!quiet)
        {
            Hide();
            _cfg.Mode = "orb";
            _cfg.Save();
        }
        Wake();
    }

    internal void ExitOrbMode()
    {
        _orb?.Hide();
        _popup.HideNow();
        Show();
        BringToFront();
        _cfg.Mode = "window";
        _cfg.Save();
        Wake();
    }

    // ------------------------------------------------------------------ 托盘 / 热键
    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => { if (_orb?.Visible == true) ExitOrbMode(); else { Show(); BringToFront(); Activate(); } });
        menu.Items.Add("悬浮球模式", null, (_, _) => EnterOrbMode());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("剪贴板解码", null, (_, _) => DoClipboard());
        menu.Items.Add("截图解码", null, (_, _) => DoShot());
        menu.Items.Add("打开图片…", null, (_, _) => DoFile());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("图库…", null, (_, _) => OpenGallery());
        menu.Items.Add("设置…", null, (_, _) => OpenSettings());
        var miPw = new ToolStripMenuItem("记住密码") { Checked = _cfg.SavePassword, CheckOnClick = true };
        miPw.CheckedChanged += (_, _) => { _cfg.SavePassword = miPw.Checked; _cfg.Save(); };
        var miTop = new ToolStripMenuItem("窗口置顶") { Checked = _cfg.TopMost, CheckOnClick = true };
        miTop.CheckedChanged += (_, _) => { _cfg.TopMost = miTop.Checked; TopMost = miTop.Checked; _cfg.Save(); };
        var miLow = new ToolStripMenuItem("空闲自动降耗") { Checked = _cfg.LowPower, CheckOnClick = true };
        miLow.CheckedChanged += (_, _) => { _cfg.LowPower = miLow.Checked; if (!_cfg.LowPower) Wake(); _cfg.Save(); };
        menu.Items.Add(miPw);
        menu.Items.Add(miTop);
        menu.Items.Add(miLow);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = IconFactory.AppIcon,
            Text = "CX 快速解码（双击显示）",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) =>
        {
            if (_orb?.Visible == true) ExitOrbMode();
            else { Show(); BringToFront(); Activate(); }
        };
    }

    internal void OpenGallery()
    {
        Wake();
        try
        {
            if (_gallery != null && !_gallery.IsDisposed)
            {
                _gallery.Activate();
                return;
            }
            AppConfig.Log($"gallery open: items={Gallery.Items.Count}");
            _gallery = new GalleryForm();
            _gallery.FormClosed += (_, _) => { _gallery = null; AppConfig.Log("gallery closed"); };
            _gallery.Show();
            _gallery.BringToFront();
            _gallery.Activate();
            AppConfig.Log("gallery shown");
        }
        catch (Exception ex)
        {
            AppConfig.Log("gallery open failed: " + ex);
        }
    }

    internal ContextMenuStrip? TrayMenu => _tray?.ContextMenuStrip;

    internal void OpenSettings()
    {
        Wake();
        using var dlg = new SettingsForm(_cfg);
        dlg.ShowDialog(_orb?.Visible == true ? null : this);
        if (!dlg.Saved) return;
        ApplyConfig();
        SetStatus("设置已保存", COk);
    }

    internal void ApplyConfig()
    {
        TopMost = _cfg.TopMost;
        _orb?.ApplyLook();
        RegisterHotkeys();
        if (!_cfg.LowPower) Wake();
    }

    private void RegisterHotkeys()
    {
        Native.UnregisterHotKey(Handle, HK_CLIP);
        Native.UnregisterHotKey(Handle, HK_SHOT);
        Native.UnregisterHotKey(Handle, HK_FILE);
        _hotkeyLabels.Clear();

        bool TryOne(int id, string text, Keys fallbackKey, bool primarySlot)
        {
            if (!HotkeyText.TryParse(text, out uint mods, out Keys key))
            {
                mods = Native.MOD_CONTROL | Native.MOD_ALT;
                key = fallbackKey;
            }
            uint[] altMods =
            {
                mods,
                Native.MOD_CONTROL | Native.MOD_SHIFT,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT
            };
            foreach (uint m in altMods)
            {
                if (m == 0) continue;
                if (!Native.RegisterHotKey(Handle, id, m, (uint)key)) continue;
                _hotkeyLabels[id] = HotkeyText.Format(m, key);
                return m == mods;
            }
            return false;
        }

        bool a = TryOne(HK_CLIP, _cfg.HotkeyClip, Keys.Q, true);
        bool b = TryOne(HK_SHOT, _cfg.HotkeyShot, Keys.S, false);
        bool c = TryOne(HK_FILE, _cfg.HotkeyFile, Keys.O, false);

        if (_hotkeyLabels.Count == 0)
            SetStatus("全局热键全被占用（点按钮照样能用）", CBad);
        else if (!(a && b && c))
            SetStatus("部分热键被占用，已改用：" + string.Join(" / ", _hotkeyLabels.Values), CWarn);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HK_CLIP: DoClipboard(); return;
                case HK_SHOT: DoShot(); return;
                case HK_FILE: DoFile(); return;
            }
        }
        base.WndProc(ref m);
    }

    // ------------------------------------------------------------------ 三条入口
    internal void DoClipboard()
    {
        Wake();
        if (_busy) return;
        var ci = ClipboardReader.ReadFirstImage();
        if (ci.Image == null) { SetStatus(ci.Desc, CBad); return; }

        if (_lastInput != null && _lastOutputSig.Length > 0 && Sig(ci.Image) == _lastOutputSig)
        {
            SetStatus("剪贴板里是上次的结果，改用上次原图…");
            DecodeAsync(_lastInput, "上次的原图", _lastInputPath);
            return;
        }
        if (!ci.Lossless) SetStatus("剪贴板给的是位图（不是原文件/PNG），先试元数据…", CSub);
        DecodeAsync(ci.Image, ci.Desc, ci.FilePath);
    }

    internal void DoShot()
    {
        Wake();
        if (_busy) return;
        bool orbVisible = _orb?.Visible == true;
        AppConfig.Log($"DoShot start orbVisible={orbVisible}");
        if (orbVisible) _orb!.Hide(); else Hide();
        Application.DoEvents();
        try
        {
            Thread.Sleep(110);
            var shot = ScreenCapture.Capture();
            using var overlay = new ShotOverlay(shot);
            overlay.ShowDialog();
            AppConfig.Log($"DoShot overlay closed canceled={overlay.Canceled} sel={overlay.SelectionScreen}");
            if (overlay.Canceled) { SetStatus("已取消截图"); return; }
            var r = overlay.SelectionScreen;
            var local = new Rectangle(r.X - shot.VirtualBounds.X, r.Y - shot.VirtualBounds.Y, r.Width, r.Height);
            var cropped = shot.Full.CropRect(local.X, local.Y, local.Width, local.Height);
            DecodeAsync(cropped, $"截图 {cropped.W}×{cropped.H}");
        }
        catch (Exception ex) { SetStatus("截图失败：" + ex.Message, CBad); }
        finally
        {
            if (orbVisible) _orb!.Show(); else Show();
        }
    }

    private void DoFile()
    {
        Wake();
        if (_busy) return;
        using var dlg = new OpenFileDialog
        {
            Title = "选择要还原的图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) LoadAndDecode(dlg.FileName);
    }

    internal void LoadAndDecode(string path)
    {
        Wake();
        if (_busy) return;
        SetStatus("读取 " + Path.GetFileName(path) + " …");
        Task.Run(() =>
        {
            try { return ImageIo.LoadFile(path); }
            catch (Exception ex) { BeginInvoke(() => SetStatus("读图失败：" + ex.Message, CBad)); return null; }
        }).ContinueWith(t =>
        {
            var img = t.Result;
            if (img != null) BeginInvoke(() => DecodeAsync(img, Path.GetFileName(path), path));
        });
    }

    // ------------------------------------------------------------------ 解码
    private void DecodeAsync(Rgba img, string source, string? inputPath = null)
    {
        if (inputPath != null) _lastInputPath = inputPath;
        string pw = _pw.Text;
        _lastInput = img;
        var opt = new DecodeOptions
        {
            Password = pw,
            Regions = null,
            Unit = _cfg.ManualUnit,
            SoftMode = _cfg.ManualSoft,
            SearchOffset = _cfg.SearchOffset,
            Crop = 0
        };
        _busy = true;
        SetButtonsEnabled(false);
        SetStatus($"解码中…（{source}）");

        Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            DecodeOutcome outcome;
            try { outcome = Decoder.Decode(img, opt); }
            catch (Exception ex) { outcome = new DecodeOutcome { Ok = false, Message = "解码异常：" + ex.Message }; }
            sw.Stop();
            outcome.Ms = sw.ElapsedMilliseconds;
            return outcome;
        }).ContinueWith(t => BeginInvoke(() => OnDecoded(t.Result, source)));
    }

    private void OnDecoded(DecodeOutcome r, string source)
    {
        _busy = false;
        SetButtonsEnabled(true);
        Wake();

        if (!r.Ok || r.Image == null)
        {
            SetStatus("✕ " + r.Message + $"（{r.Ms} ms）", CBad);
            if (r.PasswordWrong)
            {
                _pw.BackColor = Color.FromArgb(60, 26, 30);
                if (Visible) { _pw.Focus(); _pw.SelectAll(); }
            }
            return;
        }

        _pw.BackColor = Color.FromArgb(15, 19, 25);

        // 截图救不回来时：屏幕上的图被缩放，元数据没了 → 用上次的原文件兜底
        if (r.Source == "manual" && r.Score > 25 && source.StartsWith("截图"))
        {
            string? f = _lastInputPath;
            if (f == null || !File.Exists(f)) f = ClipboardReader.TryGetFirstImageFile();
            if (f != null && File.Exists(f))
            {
                try
                {
                    var fileImg = ImageIo.LoadFile(f);
                    SetStatus("屏幕上的图不是 1:1 → 改用原文件 " + Path.GetFileName(f) + " 重解…", CWarn);
                    DecodeAsync(fileImg, "原文件 " + Path.GetFileName(f), f);
                    return;
                }
                catch { }
            }
            SetStatus("⚠ 截图救不回来（屏幕上的图不是 1:1）→ 请复制原图或拖文件", CBad);
            AppConfig.Log("screenshot failed: not 1:1");
            return;
        }

        _lastResult = r.Image;
        _lastInfo = $"{r.Message} · {r.Image.W}×{r.Image.H} · {r.Ms} ms";
        try { _lastOutputSig = Sig(r.Image); } catch { _lastOutputSig = ""; }

        string whereKind = r.Source == "meta" ? "元数据" : "按区域猜";
        Gallery.Add(r.Image, $"{whereKind} · {r.Ms}ms");

        string copyMsg = "";
        if (_cfg.AutoCopy) copyMsg = ClipboardReader.PutImage(r.Image) ? "已复制" : "复制失败";

        string where = r.Source == "meta" ? "元数据" : "按区域猜";
        SetStatus($"✔ {r.Image.W}×{r.Image.H} · {r.Ms} ms · {where}"
                  + (copyMsg.Length > 0 ? " · " + copyMsg : ""), r.Source == "meta" ? COk : CWarn);

        ShowResultPopup(r);
    }

    /// <summary>悬浮球被拖动时，让结果浮层跟着走并按新位置切换上下。</summary>
    internal void RepositionPopup()
    {
        if (!_popup.Visible) return;
        _popup.Reposition(_orb?.Visible == true ? _orb.Bounds : Bounds);
    }

    private void ShowResultPopup(DecodeOutcome r)
    {
        if (!_cfg.ShowPopup || r.Image == null) return;
        var anchor = (_orb?.Visible == true) ? _orb.Bounds : Bounds;
        string info = $"{r.Image.W}×{r.Image.H} · {r.Ms} ms · "
                      + (r.Source == "meta" ? "元数据路径" : "无元数据 · 按区域猜");
        _popup.ShowResult(r.Image, info, anchor, preferAbove: true, seconds: _cfg.PopupSeconds);
    }

    private void ShowPreview()
    {
        if (_lastResult == null) return;
        Wake();
        _popup.HideNow();
        var f = new PreviewForm(_lastResult, _lastInfo);
        f.Show();
    }

    private void SetStatus(string text, Color? color = null)
    {
        _status.ForeColor = color ?? CSub;
        _status.Text = text.Replace("\r\n", " ").Replace('\n', ' ');
    }

    private void SetButtonsEnabled(bool on) => _btnClip.Enabled = _btnShot.Enabled = _btnFile.Enabled = on;

    private static string Sig(Rgba img)
    {
        var sb = new System.Text.StringBuilder(64);
        sb.Append(img.W).Append('x').Append(img.H).Append(':');
        int step = Math.Max(1, (img.W * img.H) / 512);
        for (long i = 0; i < (long)img.W * img.H; i += step)
        {
            int o = (int)i * 4;
            sb.Append(img.Data[o]).Append(',').Append(img.Data[o + 1]).Append(',')
              .Append(img.Data[o + 2]).Append(';');
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ 降耗
    private void Wake()
    {
        _lastActive = DateTime.UtcNow;
        if (_lowPower)
        {
            _lowPower = false;
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { }
        }
        if (!_idleTimer.Enabled) _idleTimer.Start();
    }

    private void EnterLowPower()
    {
        _idleTimer.Stop();
        _lowPower = true;
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; } catch { }
        try { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); } catch { }
        try { Native.EmptyWorkingSet(Native.GetCurrentProcess()); } catch { }
        if (Visible && !_busy) SetStatus("空闲 · 已降耗（鼠标进入或热键唤醒）");
    }

    // ------------------------------------------------------------------ 退出
    internal void ExitApp()
    {
        _reallyExit = true;
        _cfg.Password = _pw.Text;
        _cfg.Save();
        _tray?.Dispose();
        _tray = null;
        Application.Exit();
    }

    private void OnClosingForm(object? sender, FormClosingEventArgs e)
    {
        try { _cfg.Password = _pw.Text; _cfg.Save(); } catch { }
        if (_reallyExit) return;
        e.Cancel = true;
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            try
            {
                _tray!.BalloonTipTitle = "CX 快速解码";
                _tray.BalloonTipText = "还在后台运行，双击托盘图标可以再打开";
                _tray.ShowBalloonTip(1200);
            }
            catch { }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _popup.Dispose();
            _idleTimer.Dispose();
            _tray?.Dispose();
            _orb?.Dispose();
            _gallery?.Dispose();
        }
        base.Dispose(disposing);
    }
}
