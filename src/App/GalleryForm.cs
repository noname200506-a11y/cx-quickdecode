// 图库窗口：本次运行解出来的所有图片（缩略图网格 + 预览/另存/复制）
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal sealed class GalleryForm : Form
{
    private readonly FlowLayoutPanel _grid;
    private readonly Label _title;
    private GalleryItem? _selected;
    private readonly Dictionary<GalleryItem, Panel> _panels = new();

    private static readonly Color CBg = Color.FromArgb(23, 27, 35);
    private static readonly Color CBg2 = Color.FromArgb(30, 36, 46);
    private static readonly Color CTxt = Color.FromArgb(226, 233, 243);
    private static readonly Color CSub = Color.FromArgb(139, 152, 172);
    private static readonly Color CAccent = Color.FromArgb(47, 108, 235);

    public GalleryForm()
    {
        Text = "CX 快速解码 · 图库";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;          // CenterScreen 在无边框+DPI 下会算偏，手动居中
        ClientSize = new Size(760, 540);
        BackColor = CBg;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        DoubleBuffered = true;
        KeyPreview = true;

        _title = new Label
        {
            Text = "本次运行解出的图片",
            ForeColor = CTxt,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            Bounds = new Rectangle(18, 10, 400, 24),
            BackColor = Color.Transparent
        };
        var close = new Label
        {
            Text = "✕",
            ForeColor = CSub,
            Font = new Font("Segoe UI", 11f),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(724, 8, 26, 24),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        close.Click += (_, _) => Close();
        var hint = new Label
        {
            Text = "双击缩略图看大图 · 右键可另存/复制/删除 · 只保留本次运行（最多 60 张）",
            ForeColor = CSub,
            Bounds = new Rectangle(18, 34, 600, 20),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 8f)
        };

        _grid = new FlowLayoutPanel
        {
            Bounds = new Rectangle(14, 60, 732, 412),
            AutoScroll = true,
            BackColor = Color.FromArgb(17, 21, 28),
            Padding = new Padding(6),
            WrapContents = true
        };

        int bx = 14;
        var bOpen = SmallButton("打开预览", bx, 484); bx += 92;
        bOpen.Click += (_, _) => OpenSelected();
        var bSave = SmallButton("另存为…", bx, 484); bx += 92;
        bSave.Click += (_, _) => SaveSelected();
        var bCopy = SmallButton("复制", bx, 484); bx += 70;
        bCopy.Click += (_, _) => CopySelected();
        var bAll = SmallButton("全部另存…", bx, 484); bx += 102;
        bAll.Click += (_, _) => SaveAll();
        var bClear = SmallButton("清空", bx, 484);
        bClear.Click += (_, _) =>
        {
            if (Gallery.Items.Count == 0) return;
            if (MessageBox.Show(this, "清空本次运行的全部图片？", "图库", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;
            Gallery.Clear();
        };
        var bClose = SmallButton("关闭", 652, 484, 90, true);
        bClose.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _title, close, hint, _grid, bOpen, bSave, bCopy, bAll, bClear, bClose });

        Gallery.Changed += Rebuild;
        Load += (_, _) =>
        {
            // 在「鼠标所在的屏幕」居中：多屏时用 PrimaryScreen 会把窗口丢到另一块屏
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
            Rebuild();
        };
        FormClosed += (_, _) => Gallery.Changed -= Rebuild;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Enter) OpenSelected();
        };
        MouseDown += DragWindow;
        _title.MouseDown += DragWindow;
        Resize += (_, _) => ApplyRegion();
        ApplyRegion();
    }

    private Button SmallButton(string text, int x, int y, int w = 86, bool primary = false)
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

    private void Rebuild()
    {
        _grid.SuspendLayout();
        foreach (Control c in _grid.Controls) c.Dispose();
        _grid.Controls.Clear();
        _panels.Clear();
        _selected = null;

        if (Gallery.Items.Count == 0)
        {
            var empty = new Label
            {
                Text = "还没有解出任何图片。\r\n用剪贴板 / 截图 / 拖文件解一张，这里就会出现在。",
                ForeColor = CSub,
                Size = new Size(560, 80),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9.5f)
            };
            _grid.Controls.Add(empty);
        }
        else
        {
            foreach (var item in Gallery.Items) _grid.Controls.Add(BuildTile(item));
        }
        _title.Text = $"本次运行解出的图片（{Gallery.Items.Count}）";
        _grid.ResumeLayout();
    }

    private Panel BuildTile(GalleryItem item)
    {
        var p = new Panel
        {
            Size = new Size(132, 152),
            Margin = new Padding(4),
            BackColor = Color.FromArgb(24, 29, 38),
            Cursor = Cursors.Hand
        };
        var pic = new PictureBox
        {
            Bounds = new Rectangle(6, 6, 120, 104),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(14, 18, 24),
            Image = item.Thumb,
            Cursor = Cursors.Hand
        };
        var cap = new Label
        {
            Text = $"{item.W}×{item.H}\r\n{item.Time:HH:mm:ss}",
            ForeColor = CSub,
            Bounds = new Rectangle(6, 114, 120, 32),
            Font = new Font("Consolas", 7.5f),
            TextAlign = ContentAlignment.MiddleLeft
        };

        EventHandler select = (_, _) => Select(item, p);
        EventHandler dbl = (_, _) => { Select(item, p); OpenSelected(); };
        MouseEventHandler menu = (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            Select(item, p);
            ShowContextMenu(p, e.Location);
        };
        p.Click += select; pic.Click += select; cap.Click += select;
        p.DoubleClick += dbl; pic.DoubleClick += dbl;
        p.MouseDown += menu; pic.MouseDown += menu; cap.MouseDown += menu;

        p.Controls.AddRange(new Control[] { pic, cap });
        _panels[item] = p;
        return p;
    }

    private void Select(GalleryItem item, Panel p)
    {
        foreach (var kv in _panels) kv.Value.BackColor = Color.FromArgb(24, 29, 38);
        p.BackColor = Color.FromArgb(38, 62, 104);
        _selected = item;
    }

    private void ShowContextMenu(Control owner, Point at)
    {
        if (_selected == null) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开预览", null, (_, _) => OpenSelected());
        menu.Items.Add("另存为…", null, (_, _) => SaveSelected());
        menu.Items.Add("复制到剪贴板", null, (_, _) => CopySelected());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("从图库移除", null, (_, _) =>
        {
            if (_selected != null) Gallery.Remove(_selected);
        });
        menu.Show(owner, at);
    }

    private void OpenSelected()
    {
        if (_selected == null) return;
        var img = Gallery.Decode(_selected);
        if (img == null) { MessageBox.Show(this, "这张图解码失败", "图库"); return; }
        new PreviewForm(img, $"{_selected.W}×{_selected.H} · {_selected.Time:HH:mm:ss} · {_selected.Info}")
            .Show(this);
    }

    private void SaveSelected()
    {
        if (_selected == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片|*.png",
            FileName = $"CX解码_{_selected.Time:yyyyMMdd_HHmmss}.png"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllBytes(dlg.FileName, _selected.Png); }
        catch (Exception ex) { MessageBox.Show(this, "保存失败：" + ex.Message, "图库"); }
    }

    private void CopySelected()
    {
        if (_selected == null) return;
        var img = Gallery.Decode(_selected);
        if (img == null) return;
        ClipboardReader.PutImage(img);
    }

    private void SaveAll()
    {
        if (Gallery.Items.Count == 0) return;
        using var dlg = new FolderBrowserDialog { Description = "选择保存本次全部图片的文件夹" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int n = 0;
        foreach (var it in Gallery.Items)
        {
            try
            {
                string name = Path.Combine(dlg.SelectedPath, $"CX解码_{it.Time:yyyyMMdd_HHmmss}_{n:00}.png");
                File.WriteAllBytes(name, it.Png);
                n++;
            }
            catch { }
        }
        MessageBox.Show(this, $"已保存 {n} 张图片", "图库");
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
