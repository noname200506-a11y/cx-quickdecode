// 操作与手势设置（仿参考图的表格：修饰键 + 修饰键 + 鼠标动作）
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CxQuickDecode.App;

internal sealed class ActionsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly List<OrbAction> _edit;
    private readonly ComboBox[] _m1 = new ComboBox[6];
    private readonly ComboBox[] _m2 = new ComboBox[6];
    private readonly ComboBox[] _g = new ComboBox[6];

    public bool Saved { get; private set; }

    private static readonly Color CBg = Color.FromArgb(23, 27, 35);
    private static readonly Color CBg2 = Color.FromArgb(30, 36, 46);
    private static readonly Color CTxt = Color.FromArgb(226, 233, 243);
    private static readonly Color CSub = Color.FromArgb(139, 152, 172);
    private static readonly Color CAccent = Color.FromArgb(47, 108, 235);

    public ActionsForm(AppConfig cfg)
    {
        _cfg = cfg;
        _edit = cfg.OrbActions.Count > 0
            ? cfg.OrbActions.Select(a => new OrbAction
            {
                Id = a.Id, Name = a.Name, Mod1 = a.Mod1, Mod2 = a.Mod2, Gesture = a.Gesture
            }).ToList()
            : OrbAction.Defaults();

        Text = "CX 快速解码 · 操作与手势";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(486, 392);
        BackColor = CBg;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        DoubleBuffered = true;
        TopMost = cfg.TopMost;

        var title = MakeLabel("操作与手势（悬浮球）", 16, 10, 300, CTxt);
        title.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        var close = MakeLabel("✕", 452, 8, 24, CSub);
        close.TextAlign = ContentAlignment.MiddleCenter;
        close.Cursor = Cursors.Hand;
        close.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { title, close });
        MouseDown += DragWindow;
        title.MouseDown += DragWindow;

        Controls.Add(MakeLabel("操作", 16, 44, 96, CSub));
        Controls.Add(MakeLabel("修饰键", 120, 44, 60, CSub));
        Controls.Add(MakeLabel("第二键", 216, 44, 54, CSub));
        Controls.Add(MakeLabel("鼠标动作", 296, 44, 80, CSub));
        var line = new Panel { Bounds = new Rectangle(16, 64, 454, 1), BackColor = Color.FromArgb(44, 52, 66) };
        Controls.Add(line);

        int y = 74;
        for (int i = 0; i < _edit.Count && i < 6; i++)
        {
            var a = _edit[i];
            Controls.Add(MakeLabel(a.Name, 16, y + 3, 100, CTxt));

            var c1 = Combo(120, y, 90, OrbAction.ModOptions, a.Mod1);
            var c2 = Combo(216, y, 74, OrbAction.ModOptions, a.Mod2);
            var c3 = Combo(296, y, 110, OrbAction.Gestures, a.Gesture);
            _m1[i] = c1; _m2[i] = c2; _g[i] = c3;
            Controls.AddRange(new Control[] { c1, c2, c3 });
            y += 36;
        }

        var tip = MakeLabel("* 如有冲突，由上至下优先匹配。左键拖动仍然是移动悬浮球（不会触发动作）。",
            16, y + 6, 454, CSub);
        tip.Font = new Font("Microsoft YaHei UI", 8f);
        Controls.Add(tip);

        var reset = MakeButton("恢复默认", 16, 344, 96);
        reset.Click += (_, _) => ResetDefaults();
        var cancel = MakeButton("取消", 262, 344, 96);
        cancel.Click += (_, _) => Close();
        var save = MakeButton("保存", 366, 344, 104, true);
        save.Click += (_, _) => DoSave();
        Controls.AddRange(new Control[] { reset, cancel, save });

        Load += (_, _) =>
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        };
        Resize += (_, _) => ApplyRegion();
        ApplyRegion();
    }

    private Label MakeLabel(string text, int x, int y, int w, Color color) => new()
    {
        Text = text,
        Bounds = new Rectangle(x, y, w, 22),
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoSize = false
    };

    private ComboBox Combo(int x, int y, int w, string[] items, string current)
    {
        var c = new ComboBox
        {
            Bounds = new Rectangle(x, y, w, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.Black
        };
        c.Items.AddRange(items);
        int idx = Array.IndexOf(items, current);
        c.SelectedIndex = idx >= 0 ? idx : 0;
        return c;
    }

    private Button MakeButton(string text, int x, int y, int w, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, w, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? CAccent : CBg2,
            ForeColor = primary ? Color.White : CTxt,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 9f)
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private void ResetDefaults()
    {
        var d = OrbAction.Defaults();
        for (int i = 0; i < _edit.Count && i < 6 && i < d.Count; i++)
        {
            _m1[i].SelectedItem = d[i].Mod1;
            _m2[i].SelectedItem = d[i].Mod2;
            _g[i].SelectedItem = d[i].Gesture;
        }
    }

    private void DoSave()
    {
        for (int i = 0; i < _edit.Count && i < 6; i++)
        {
            _edit[i].Mod1 = (string)(_m1[i].SelectedItem ?? "无");
            _edit[i].Mod2 = (string)(_m2[i].SelectedItem ?? "无");
            _edit[i].Gesture = (string)(_g[i].SelectedItem ?? "左键单击");
        }
        _cfg.OrbActions = _edit;
        _cfg.Save();
        Saved = true;
        Close();
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

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
    }
}
