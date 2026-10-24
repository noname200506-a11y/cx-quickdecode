// 设置界面：快捷键 / 结果浮层 / 悬浮球外观 / 性能 / 配置位置
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CxQuickDecode.App;

internal sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly string[] _hotkeys = new string[3];
    private readonly Label[] _hotkeyLabels = new Label[3];
    private readonly Button[] _recordButtons = new Button[3];
    private int _recording = -1;

    private readonly CheckBox _chkPopup, _chkAutoCopy, _chkLowPower, _chkTopMost;
    private readonly NumericUpDown _numPopup, _numOrb, _numIdle;
    private readonly ComboBox _cmbContent;
    private readonly TextBox _txtText;
    private readonly NumericUpDown _numTextScale, _numTextRotate, _numImageScale, _numImageRotate;
    private readonly Button _btnPickImage, _btnBgColor;
    private readonly Label _lblImagePath;
    private readonly NumericUpDown _numBgAlpha;
    private readonly TrackBar _trkOpacity;
    private readonly Label _lblOpacity;

    private string _imagePath;
    private Color _bgColor;

    public bool Saved { get; private set; }

    private static readonly Color CBg = Color.FromArgb(23, 27, 35);
    private static readonly Color CBg2 = Color.FromArgb(30, 36, 46);
    private static readonly Color CTxt = Color.FromArgb(226, 233, 243);
    private static readonly Color CSub = Color.FromArgb(139, 152, 172);
    private static readonly Color CAccent = Color.FromArgb(47, 108, 235);

    public SettingsForm(AppConfig cfg)
    {
        _cfg = cfg;
        _imagePath = cfg.OrbImagePath ?? "";
        _bgColor = Color.FromArgb(cfg.OrbBgColor);

        Text = "CX 快速解码 · 设置";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(524, 794);
        BackColor = CBg;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        KeyPreview = true;
        DoubleBuffered = true;
        TopMost = cfg.TopMost;

        _hotkeys[0] = cfg.HotkeyClip;
        _hotkeys[1] = cfg.HotkeyShot;
        _hotkeys[2] = cfg.HotkeyFile;

        var title = MakeLabel("设置", 18, 10, 220, CTxt);
        title.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        var close = MakeLabel("✕", 488, 8, 26, CSub);
        close.TextAlign = ContentAlignment.MiddleCenter;
        close.Font = new Font("Segoe UI", 11f);
        close.Cursor = Cursors.Hand;
        close.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { title, close });
        MouseDown += DragWindow;
        title.MouseDown += DragWindow;

        int y = 46;

        // ---------------- 快捷键
        AddSection("快捷键（点「录制」后直接按组合键，Esc 取消）", y); y += 26;
        string[] names = { "剪贴板解码", "截图解码", "打开图片" };
        for (int i = 0; i < 3; i++)
        {
            Controls.Add(MakeLabel(names[i], 22, y + 4, 110, CSub));
            var val = MakeLabel("", 140, y + 4, 240, CTxt);
            val.Font = new Font("Consolas", 9.5f);
            _hotkeyLabels[i] = val;
            var btn = MakeButton("录制", 396, y, 96);
            int idx = i;
            btn.Click += (_, _) => StartRecord(idx);
            _recordButtons[i] = btn;
            Controls.AddRange(new Control[] { val, btn });
            RefreshHotkeyLabel(idx);
            y += 34;
        }

        // ---------------- 结果浮层
        y += 8;
        AddSection("结果浮层", y); y += 26;
        _chkPopup = MakeCheck("显示解析结果浮层（跟随悬浮窗/球，贴在它上/下方，自动消失）", 22, y);
        _chkPopup.Checked = cfg.ShowPopup;
        Controls.Add(_chkPopup); y += 30;
        _chkAutoCopy = MakeCheck("解码成功后自动复制结果到剪贴板", 22, y);
        _chkAutoCopy.Checked = cfg.AutoCopy;
        Controls.Add(_chkAutoCopy); y += 30;
        Controls.Add(MakeLabel("浮层停留", 22, y + 3, 90, CSub));
        _numPopup = MakeNum(112, y, 84, 0.5m, 60, (decimal)Math.Clamp(cfg.PopupSeconds, 0.8, 60), 1);
        Controls.Add(_numPopup);
        Controls.Add(MakeLabel("秒（鼠标停在浮层上不消失）", 204, y + 3, 260, CSub));
        y += 40;

        // ---------------- 悬浮球
        AddSection("悬浮球（缩略模式外观）", y); y += 26;

        Controls.Add(MakeLabel("直径", 22, y + 3, 44, CSub));
        _numOrb = MakeNum(70, y, 100, 48, 160, Math.Clamp(cfg.OrbSize, 48, 160), 0);
        Controls.Add(_numOrb);
        Controls.Add(MakeLabel("px", 176, y + 3, 26, CSub));
        Controls.Add(MakeLabel("内容", 220, y + 3, 40, CSub));
        _cmbContent = new ComboBox
        {
            Bounds = new Rectangle(264, y, 150, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = CBg2,
            ForeColor = CTxt
        };
        _cmbContent.Items.AddRange(new object[] { "默认图标", "文字", "自定义图片" });
        _cmbContent.SelectedIndex = cfg.OrbContent switch { "text" => 1, "image" => 2, _ => 0 };
        Controls.Add(_cmbContent);
        y += 34;

        Controls.Add(MakeLabel("文本", 22, y + 3, 40, CSub));
        _txtText = new TextBox
        {
            Bounds = new Rectangle(66, y, 132, 26),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Color.Black,
            Text = string.IsNullOrEmpty(cfg.OrbText) ? "CX" : cfg.OrbText
        };
        Controls.Add(_txtText);
        Controls.Add(MakeLabel("缩放", 206, y + 3, 36, CSub));
        _numTextScale = MakeNum(244, y, 70, 0.4m, 1.6m, (decimal)Math.Clamp(cfg.OrbTextScale, 0.4, 1.6), 2, 0.05m);
        Controls.Add(_numTextScale);
        Controls.Add(MakeLabel("旋转", 320, y + 3, 36, CSub));
        _numTextRotate = MakeNum(358, y, 74, -180, 180, Math.Clamp(cfg.OrbTextRotate, -180, 180), 0);
        Controls.Add(_numTextRotate);
        y += 34;

        Controls.Add(MakeLabel("图片", 22, y + 3, 40, CSub));
        _btnPickImage = MakeButton("选择…", 66, y, 84);
        _btnPickImage.Click += (_, _) => PickImage();
        _lblImagePath = MakeLabel("", 156, y + 4, 336, CSub);
        _lblImagePath.Font = new Font("Consolas", 8f);
        _lblImagePath.AutoEllipsis = true;
        RefreshImageLabel();
        Controls.AddRange(new Control[] { _btnPickImage, _lblImagePath });
        y += 34;

        Controls.Add(MakeLabel("图片缩放", 22, y + 3, 62, CSub));
        _numImageScale = MakeNum(88, y, 70, 0.3m, 1.6m, (decimal)Math.Clamp(cfg.OrbImageScale, 0.3, 1.6), 2, 0.05m);
        Controls.Add(_numImageScale);
        Controls.Add(MakeLabel("旋转", 164, y + 3, 36, CSub));
        _numImageRotate = MakeNum(202, y, 70, -180, 180, Math.Clamp(cfg.OrbImageRotate, -180, 180), 0);
        Controls.Add(_numImageRotate);
        Controls.Add(MakeLabel("背景", 280, y + 3, 36, CSub));
        _btnBgColor = MakeButton("", 318, y, 44);
        _btnBgColor.BackColor = _bgColor;
        _btnBgColor.Click += (_, _) => PickBgColor();
        Controls.Add(_btnBgColor);
        Controls.Add(MakeLabel("不透明度", 370, y + 3, 56, CSub));
        _numBgAlpha = MakeNum(430, y, 62, 0, 100, Math.Clamp((int)Math.Round(cfg.OrbBgAlpha * 100.0 / 255), 0, 100), 0);
        Controls.Add(_numBgAlpha);
        Controls.Add(MakeLabel("%", 496, y + 3, 20, CSub));
        y += 34;

        Controls.Add(MakeLabel("整体不透明度", 22, y + 3, 92, CSub));
        _trkOpacity = new TrackBar
        {
            Bounds = new Rectangle(118, y - 6, 300, 34),
            Minimum = 20,
            Maximum = 100,
            TickFrequency = 10,
            Value = Math.Clamp(cfg.OrbOpacity, 20, 100),
            BackColor = CBg
        };
        _lblOpacity = MakeLabel($"{_trkOpacity.Value}%", 428, y + 3, 56, CSub);
        _trkOpacity.Scroll += (_, _) => _lblOpacity.Text = $"{_trkOpacity.Value}%";
        Controls.AddRange(new Control[] { _trkOpacity, _lblOpacity });
        y += 40;

        var bActs = MakeButton("操作与手势…", 22, y, 132);
        bActs.Click += (_, _) =>
        {
            using var f = new ActionsForm(_cfg);
            f.ShowDialog(this);
            // 保存后立刻生效：悬浮球每次手势都重新读 cfg.OrbActions
        };
        Controls.Add(bActs);
        Controls.Add(MakeLabel("右键/中键/双击各自做什么，都在这里配（仿表格式）", 164, y + 4, 336, CSub));
        y += 42;

        // ---------------- 性能
        AddSection("性能与窗口", y); y += 26;
        _chkLowPower = MakeCheck("空闲自动降耗", 22, y);
        _chkLowPower.Checked = cfg.LowPower;
        Controls.Add(_chkLowPower);
        _numIdle = MakeNum(160, y, 66, 3, 600, Math.Clamp(cfg.IdleSeconds, 3, 600), 0);
        Controls.Add(_numIdle);
        Controls.Add(MakeLabel("秒无操作后降低优先级 / 收缩内存", 234, y + 3, 280, CSub));
        y += 32;
        _chkTopMost = MakeCheck("窗口与悬浮球置顶", 22, y);
        _chkTopMost.Checked = cfg.TopMost;
        Controls.Add(_chkTopMost);
        y += 36;

        // ---------------- 配置
        AddSection("配置文件", y); y += 26;
        var path = MakeLabel(AppConfig.ConfigPath, 22, y, 478, CSub);
        path.Font = new Font("Consolas", 8.5f);
        path.AutoEllipsis = true;
        Controls.Add(path);
        y += 26;
        var openDir = MakeButton("打开文件夹", 22, y, 110);
        openDir.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", AppConfig.ConfigDir); } catch { }
        };
        var reset = MakeButton("恢复默认", 144, y, 110);
        reset.Click += (_, _) => ResetDefaults();
        Controls.AddRange(new Control[] { openDir, reset });

        // ---------------- 底部
        var cancel = MakeButton("取消", 294, 748, 96);
        cancel.Click += (_, _) => Close();
        var save = MakeButton("保存", 402, 748, 96, true);
        save.Click += (_, _) => DoSave();
        Controls.AddRange(new Control[] { cancel, save });

        Load += (_, _) =>
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        };
        KeyDown += OnKeyDown;
        Resize += (_, _) => ApplyRegion();
        ApplyRegion();
    }

    // ------------------------------------------------------------------ 控件工厂
    private Label MakeLabel(string text, int x, int y, int w, Color color) => new()
    {
        Text = text,
        Bounds = new Rectangle(x, y, w, 22),
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = false
    };

    private Button MakeButton(string text, int x, int y, int w, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, w, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? CAccent : CBg2,
            ForeColor = primary ? Color.White : CTxt,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 9f)
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private CheckBox MakeCheck(string text, int x, int y) => new()
    {
        Text = text,
        Bounds = new Rectangle(x, y, 480, 24),
        ForeColor = CTxt,
        BackColor = Color.Transparent,
        FlatStyle = FlatStyle.Standard,
        UseVisualStyleBackColor = false
    };

    private NumericUpDown MakeNum(int x, int y, int w, decimal min, decimal max, decimal val, int decimals,
                                  decimal? inc = null) => new()
    {
        Bounds = new Rectangle(x, y, w, 26),
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(val, min, max),
        DecimalPlaces = decimals,
        Increment = inc ?? (decimals > 0 ? 0.5m : 1m),
        BackColor = Color.White,
        ForeColor = Color.Black,
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = HorizontalAlignment.Right
    };

    private void AddSection(string text, int y)
    {
        var l = new Label
        {
            Text = text,
            Bounds = new Rectangle(18, y, 480, 20),
            ForeColor = Color.FromArgb(120, 170, 255),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
        };
        var line = new Panel
        {
            Bounds = new Rectangle(18, y + 22, 488, 1),
            BackColor = Color.FromArgb(44, 52, 66)
        };
        Controls.AddRange(new Control[] { l, line });
    }

    private void RefreshHotkeyLabel(int i) => _hotkeyLabels[i].Text = _hotkeys[i];

    private void RefreshImageLabel()
    {
        _lblImagePath.Text = string.IsNullOrWhiteSpace(_imagePath)
            ? "（未选择，支持 png / jpg / bmp / gif / svg）"
            : Path.GetFileName(_imagePath);
        _lblImagePath.ForeColor = string.IsNullOrWhiteSpace(_imagePath) ? CSub : CTxt;
    }

    // ------------------------------------------------------------------ 选图 / 选色
    private void PickImage()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择悬浮球图标",
            Filter = "图标|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.svg|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _imagePath = dlg.FileName;
        _cmbContent.SelectedIndex = 2;      // 选了图就自动切到"自定义图片"
        RefreshImageLabel();
    }

    private void PickBgColor()
    {
        using var dlg = new ColorDialog { Color = _bgColor, FullOpen = true, AnyColor = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _bgColor = dlg.Color;
        _btnBgColor.BackColor = _bgColor;
        _btnBgColor.Text = "";
    }

    // ------------------------------------------------------------------ 录制
    private void StartRecord(int idx)
    {
        _recording = idx;
        for (int i = 0; i < 3; i++) _recordButtons[i].Text = i == idx ? "按键…" : "录制";
        _hotkeyLabels[idx].Text = "…";
        Activate();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_recording < 0) return;
        if (e.KeyCode == Keys.Escape)
        {
            _recording = -1;
            for (int i = 0; i < 3; i++) { _recordButtons[i].Text = "录制"; RefreshHotkeyLabel(i); }
            return;
        }
        if (!HotkeyText.Capture(e.KeyData, out uint mods, out Keys key)) return;
        _hotkeys[_recording] = HotkeyText.Format(mods, key);
        RefreshHotkeyLabel(_recording);
        _recordButtons[_recording].Text = "录制";
        _recording = -1;
        e.SuppressKeyPress = true;
    }

    // ------------------------------------------------------------------ 保存
    private void DoSave()
    {
        _cfg.HotkeyClip = _hotkeys[0];
        _cfg.HotkeyShot = _hotkeys[1];
        _cfg.HotkeyFile = _hotkeys[2];
        _cfg.ShowPopup = _chkPopup.Checked;
        _cfg.AutoCopy = _chkAutoCopy.Checked;
        _cfg.PopupSeconds = (double)_numPopup.Value;

        _cfg.OrbSize = (int)_numOrb.Value;
        _cfg.OrbOpacity = _trkOpacity.Value;
        _cfg.OrbContent = _cmbContent.SelectedIndex switch { 1 => "text", 2 => "image", _ => "icon" };
        _cfg.OrbText = string.IsNullOrWhiteSpace(_txtText.Text) ? "CX" : _txtText.Text.Trim();
        _cfg.OrbTextScale = (double)_numTextScale.Value;
        _cfg.OrbTextRotate = (int)_numTextRotate.Value;
        _cfg.OrbImagePath = _imagePath ?? "";
        _cfg.OrbImageScale = (double)_numImageScale.Value;
        _cfg.OrbImageRotate = (int)_numImageRotate.Value;
        _cfg.OrbBgColor = _bgColor.ToArgb();
        _cfg.OrbBgAlpha = (int)Math.Round((double)_numBgAlpha.Value * 255 / 100);

        _cfg.LowPower = _chkLowPower.Checked;
        _cfg.IdleSeconds = (int)_numIdle.Value;
        _cfg.TopMost = _chkTopMost.Checked;
        _cfg.Save();
        Saved = true;
        Close();
    }

    private void ResetDefaults()
    {
        var d = new AppConfig();
        _hotkeys[0] = d.HotkeyClip;
        _hotkeys[1] = d.HotkeyShot;
        _hotkeys[2] = d.HotkeyFile;
        for (int i = 0; i < 3; i++) RefreshHotkeyLabel(i);
        _chkPopup.Checked = d.ShowPopup;
        _chkAutoCopy.Checked = d.AutoCopy;
        _numPopup.Value = (decimal)d.PopupSeconds;
        _numOrb.Value = d.OrbSize;
        _trkOpacity.Value = d.OrbOpacity;
        _lblOpacity.Text = d.OrbOpacity + "%";
        _cmbContent.SelectedIndex = 0;
        _txtText.Text = d.OrbText;
        _numTextScale.Value = (decimal)d.OrbTextScale;
        _numTextRotate.Value = d.OrbTextRotate;
        _imagePath = "";
        RefreshImageLabel();
        _numImageScale.Value = (decimal)d.OrbImageScale;
        _numImageRotate.Value = d.OrbImageRotate;
        _bgColor = Color.FromArgb(d.OrbBgColor);
        _btnBgColor.BackColor = _bgColor;
        _numBgAlpha.Value = (decimal)Math.Round(d.OrbBgAlpha * 100.0 / 255);
        _chkLowPower.Checked = d.LowPower;
        _numIdle.Value = d.IdleSeconds;
        _chkTopMost.Checked = d.TopMost;
    }

    // ------------------------------------------------------------------ 外观
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

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
    }
}
