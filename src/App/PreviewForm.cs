// 结果预览：可缩放查看、复制、另存为
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal sealed class PreviewForm : Form
{
    private readonly Rgba _img;
    private readonly Bitmap _bmp;
    private readonly string _info;
    private readonly Panel _panel;
    private readonly PictureBox _pb;
    private readonly Label _status;
    private bool _fit = true;

    private Point _dragStart;
    private bool _dragging;

    public PreviewForm(Rgba img, string info)
    {
        _img = img;
        _bmp = ImageIo.ToBitmap(img);
        _info = info;

        Text = "CX 还原结果";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(900, 660);
        BackColor = Color.FromArgb(20, 24, 31);
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = false;

        var title = new Label
        {
            Text = "CX 还原结果",
            ForeColor = Color.FromArgb(200, 214, 232),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            AutoSize = false,
            Bounds = new Rectangle(14, 8, 240, 22),
            BackColor = Color.Transparent
        };
        title.MouseDown += DragStart;
        title.MouseMove += DragMove;
        title.MouseUp += (_, _) => _dragging = false;

        var close = new Label
        {
            Text = "✕",
            ForeColor = Color.FromArgb(170, 185, 205),
            Font = new Font("Segoe UI", 11f),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(866, 6, 26, 24),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        close.Click += (_, _) => Close();
        close.MouseEnter += (_, _) => close.ForeColor = Color.White;
        close.MouseLeave += (_, _) => close.ForeColor = Color.FromArgb(170, 185, 205);

        _panel = new Panel
        {
            Bounds = new Rectangle(10, 34, 880, 556),
            BackColor = Color.FromArgb(12, 15, 20),
            AutoScroll = true
        };
        _pb = new PictureBox
        {
            Image = _bmp,
            SizeMode = PictureBoxSizeMode.Zoom,
            Bounds = new Rectangle(0, 0, _panel.ClientSize.Width, _panel.ClientSize.Height),
            BackColor = Color.FromArgb(12, 15, 20)
        };
        _pb.DoubleClick += (_, _) => ToggleFit();
        _panel.Controls.Add(_pb);
        _panel.Resize += (_, _) => { if (_fit) LayoutFit(); };

        var bar = new Panel
        {
            Bounds = new Rectangle(10, 594, 880, 40),
            BackColor = Color.FromArgb(26, 31, 40)
        };
        _status = new Label
        {
            Text = _info,
            ForeColor = Color.FromArgb(150, 165, 186),
            AutoSize = false,
            Bounds = new Rectangle(12, 8, 520, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        var bFit = MakeButton("适应", 540, 7);
        bFit.Click += (_, _) => ToggleFit();
        var bOne = MakeButton("1:1", 600, 7);
        bOne.Click += (_, _) => { _fit = false; _pb.SizeMode = PictureBoxSizeMode.Normal; _pb.Bounds = new Rectangle(0, 0, _img.W, _img.H); };
        var bCopy = MakeButton("复制", 660, 7);
        bCopy.Click += (_, _) =>
        {
            ClipboardReader.PutImage(_img);
            _status.Text = "已复制到剪贴板";
        };
        var bSave = MakeButton("另存为…", 720, 7, 84, true);
        bSave.Click += (_, _) => SaveAs();
        bar.Controls.AddRange(new Control[] { _status, bFit, bOne, bCopy, bSave });

        Controls.AddRange(new Control[] { title, close, _panel, bar });

        MouseDown += DragStart;
        MouseMove += DragMove;
        MouseUp += (_, _) => _dragging = false;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            if (e.KeyCode == Keys.S && e.Control) SaveAs();
            if (e.KeyCode == Keys.Home) ToggleFit();
        };
        // 滚轮：缩放
        MouseWheel += (_, e) => Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15);
        _panel.MouseWheel += (_, e) => Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15);
        _pb.MouseWheel += (_, e) => Zoom(e.Delta > 0 ? 1.15 : 1 / 1.15);
        Shown += (_, _) =>
        {
            var wa2 = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa2.Left + (wa2.Width - Width) / 2, wa2.Top + (wa2.Height - Height) / 2);
            LayoutFit();
        };
        Resize += (_, _) => ApplyRegion();
        ApplyRegion();
    }

    private Button MakeButton(string text, int x, int y, int w = 56, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, w, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(47, 108, 235) : Color.FromArgb(38, 45, 58),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

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

    private void ToggleFit()
    {
        _fit = !_fit;
        if (_fit) LayoutFit();
        else
        {
            _pb.SizeMode = PictureBoxSizeMode.Normal;
            _pb.Bounds = new Rectangle(0, 0, _img.W, _img.H);
        }
    }

    private void LayoutFit()
    {
        _fit = true;
        _pb.SizeMode = PictureBoxSizeMode.Zoom;
        _pb.Bounds = new Rectangle(0, 0, Math.Max(1, _panel.ClientSize.Width - 1), Math.Max(1, _panel.ClientSize.Height - 1));
    }

    private void Zoom(double f)
    {
        _fit = false;
        _pb.SizeMode = PictureBoxSizeMode.Normal;
        int w = Math.Clamp((int)(_pb.Width * f), 32, _img.W * 8);
        int h = Math.Clamp((int)(_pb.Height * f), 32, _img.H * 8);
        _pb.Bounds = new Rectangle(_pb.Left, _pb.Top, w, h);
        string msg = $"缩放 {w * 100 / Math.Max(1, _img.W)}%";
        _status.Text = msg;
    }

    private void DragStart(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; }
    }

    private void DragMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Left += e.X - _dragStart.X;
        Top += e.Y - _dragStart.Y;
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
            _status.Text = "已保存：" + dlg.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, "保存失败：" + ex.Message, "CX 快速解码"); }
    }
}
