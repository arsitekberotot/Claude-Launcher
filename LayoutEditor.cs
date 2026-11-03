using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

static class EditorStyle
{
    public static readonly Font Icon = new Font("Segoe MDL2 Assets", 10f);
    public static readonly Font Section = new Font("Segoe UI Semibold", 13f);
    public static readonly Font ZoneNumber = new Font("Segoe UI Semibold", 26f);
    public static readonly Color TopBar = Color.FromArgb(10, 10, 10);
    public static readonly Color Canvas = Color.FromArgb(10, 10, 10);
    public static readonly Color GridLine = Color.FromArgb(22, 22, 22);
    public const string EditGlyph = "", DeleteGlyph = "", CloseGlyph = "";

    public static void EnableDoubleBuffer(Control c)
    {
        typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(c, true, null);
    }
}

// ---------------------------------------------------------------- tiles & cards

class MonitorTile : Control
{
    public readonly MonitorInfo Monitor;
    bool selected, hover;

    public bool Selected
    {
        get { return selected; }
        set { selected = value; Invalidate(); }
    }

    public MonitorTile(MonitorInfo monitor)
    {
        Monitor = monitor;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(Theme.S(160), Theme.S(88));
        Margin = new Padding(Theme.S(8));
        Cursor = Cursors.Hand;
        BackColor = EditorStyle.TopBar;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), Theme.S(20)))
        using (var b = new SolidBrush(hover ? Theme.SurfaceHover : Theme.Surface))
        using (var pen = new Pen(selected ? Theme.Accent : Theme.Border, selected ? 1.5f : 1))
        {
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }
        var color = selected ? Theme.Accent : Theme.Text;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, Monitor.Number.ToString(), Theme.Title, new Rectangle(0, Theme.S(8), Width, Theme.S(32)), color, flags);
        TextRenderer.DrawText(g, Monitor.Bounds.Width + " × " + Monitor.Bounds.Height, Theme.Small,
            new Rectangle(0, Theme.S(44), Width, Theme.S(18)), selected ? Theme.Accent : Theme.Muted, flags);
        TextRenderer.DrawText(g, (int)Math.Round(Monitor.Scale * 100) + "%" + (Monitor.Primary ? "  ·  utama" : ""), Theme.Small,
            new Rectangle(0, Theme.S(62), Width, Theme.S(18)), selected ? Theme.Accent : Theme.Muted, flags);
    }
}

class LayoutCard : Control
{
    public readonly string Key;
    readonly string title;
    readonly Func<List<RectangleF>> zones;
    readonly bool editable;
    public Action OnSelect, OnEdit, OnDelete;
    bool selected, hover;
    Rectangle editRect, deleteRect;

    public bool Selected
    {
        get { return selected; }
        set { selected = value; Invalidate(); }
    }

    public LayoutCard(string key, string title, Func<List<RectangleF>> zones, bool editable)
    {
        Key = key;
        this.title = title;
        this.zones = zones;
        this.editable = editable;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(Theme.S(210), Theme.S(164));
        Margin = new Padding(Theme.S(6));
        Cursor = Cursors.Hand;
        BackColor = Theme.Bg;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (editable && editRect.Contains(e.Location)) { if (OnEdit != null) OnEdit(); }
        else if (editable && deleteRect.Contains(e.Location)) { if (OnDelete != null) OnDelete(); }
        else if (OnSelect != null) OnSelect();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), Theme.S(20)))
        using (var b = new SolidBrush(hover ? Theme.SurfaceHover : Theme.Surface))
        using (var pen = new Pen(selected ? Theme.Accent : Theme.Border, selected ? 1.5f : 1))
        {
            g.FillPath(b, path);
            g.DrawPath(pen, path);
        }

        int pad = Theme.S(14);
        var list = zones();
        int iconSize = Theme.S(26);
        int titleRight = Width - pad;
        if (editable)
        {
            deleteRect = new Rectangle(Width - pad - iconSize + Theme.S(4), Theme.S(9), iconSize, iconSize);
            editRect = new Rectangle(deleteRect.Left - iconSize, Theme.S(9), iconSize, iconSize);
            titleRight = editRect.Left - Theme.S(4);
            var iconFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, EditorStyle.EditGlyph, EditorStyle.Icon, editRect, Theme.Muted, iconFlags);
            TextRenderer.DrawText(g, EditorStyle.DeleteGlyph, EditorStyle.Icon, deleteRect, Theme.Muted, iconFlags);
        }
        var text = title + "  ·  " + list.Count + " zona";
        TextRenderer.DrawText(g, text, Theme.Name, new Rectangle(pad, Theme.S(12), titleRight - pad, Theme.S(22)),
            selected ? Theme.Accent : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var preview = new Rectangle(pad, Theme.S(44), Width - 2 * pad, Height - Theme.S(44) - pad);
        Layouts.DrawPreview(g, preview, list, Theme.SurfaceHover);
    }
}

// ---------------------------------------------------------------- layout editor

class LayoutEditorForm : Form
{
    readonly LayoutSettings settings;
    readonly List<Profile> profiles;
    readonly List<MonitorInfo> monitors;
    readonly List<ZoneLayout> customs;
    readonly HashSet<string> ids;
    readonly FlowLayoutPanel monitorRow, instanceRow, templateRow, customRow, body;
    readonly DarkCheck gapCheck;
    readonly List<LayoutCard> cards = new List<LayoutCard>();
    readonly List<MonitorTile> tiles = new List<MonitorTile>();
    string monitorDevice, layoutKey;

    public LayoutEditorForm(LayoutSettings settings, List<Profile> profiles)
    {
        this.settings = settings;
        this.profiles = profiles;
        monitors = WindowArranger.Monitors();
        customs = Layouts.LoadCustom();
        var monitor = monitors.FirstOrDefault(m => m.Device == settings.Monitor)
            ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
        monitorDevice = monitor == null ? "" : monitor.Device;
        layoutKey = settings.Layout;
        ids = new HashSet<string>(profiles.Where(settings.Includes).Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        Text = "Tata letak jendela";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ClientSize = new Size(Theme.S(1000), Theme.S(760));
        MinimumSize = new Size(Theme.S(760), Theme.S(560));

        // monitors
        var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(124), BackColor = EditorStyle.TopBar };
        monitorRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = EditorStyle.TopBar };
        top.Controls.Add(monitorRow);
        top.Resize += (s, e) => CenterMonitors(top);
        monitorRow.SizeChanged += (s, e) => CenterMonitors(top);

        // footer
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(68), BackColor = Theme.Bg };
        gapCheck = new DarkCheck("Beri jarak antar jendela") { Width = Theme.S(240), Checked = settings.Gap };
        var newBtn = FlatButton.Secondary("+  Buat layout baru");
        newBtn.Size = new Size(Theme.S(176), Theme.S(36));
        newBtn.Click += (s, e) => NewCustom();
        var cancelBtn = FlatButton.Secondary("Batal");
        cancelBtn.Size = new Size(Theme.S(100), Theme.S(36));
        cancelBtn.Click += (s, e) => DialogResult = DialogResult.Cancel;
        var saveBtn = FlatButton.Primary("Simpan");
        saveBtn.Size = new Size(Theme.S(100), Theme.S(36));
        saveBtn.Click += (s, e) => Save();
        foreach (var b in new[] { newBtn, cancelBtn, saveBtn }) b.BackColor = Theme.Bg;
        bottom.Controls.AddRange(new Control[] { gapCheck, newBtn, cancelBtn, saveBtn });
        bottom.Resize += (s, e) =>
        {
            int pad = Theme.S(20), y = (bottom.Height - Theme.S(36)) / 2;
            gapCheck.Location = new Point(pad, (bottom.Height - gapCheck.Height) / 2);
            saveBtn.Location = new Point(bottom.Width - pad - saveBtn.Width, y);
            cancelBtn.Location = new Point(saveBtn.Left - Theme.S(8) - cancelBtn.Width, y);
            newBtn.Location = new Point(cancelBtn.Left - Theme.S(16) - newBtn.Width, y);
        };

        // sections
        body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Bg,
            Padding = new Padding(Theme.S(18), Theme.S(6), Theme.S(18), Theme.S(12))
        };
        EditorStyle.EnableDoubleBuffer(body);
        body.HandleCreated += (s, e) => Native.DarkScrollbars(body.Handle);
        body.Controls.Add(Section("Instance"));
        body.Controls.Add(Hint("Instance yang dibuka & dirapikan. Nomor zona mengikuti urutan di daftar launcher."));
        instanceRow = Row();
        body.Controls.Add(instanceRow);
        body.Controls.Add(Section("Template"));
        templateRow = Row();
        body.Controls.Add(templateRow);
        body.Controls.Add(Section("Custom"));
        customRow = Row();
        body.Controls.Add(customRow);
        body.Resize += (s, e) => FitRows();

        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(top);

        BuildMonitors();
        BuildInstances();
        BuildCards();
    }

    int ZoneCount { get { return Math.Max(1, ids.Count); } }

    Size WorkSize
    {
        get
        {
            var m = monitors.FirstOrDefault(x => x.Device == monitorDevice);
            return m == null ? new Size(16, 9) : m.Work.Size;
        }
    }

    static Label Section(string text)
    {
        return new Label { Text = text, Font = EditorStyle.Section, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(Theme.S(6), Theme.S(16), 0, Theme.S(6)) };
    }

    static Label Hint(string text)
    {
        return new Label { Text = text, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, UseMnemonic = false, Margin = new Padding(Theme.S(6), 0, 0, Theme.S(6)) };
    }

    static FlowLayoutPanel Row()
    {
        return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0) };
    }

    void CenterMonitors(Panel top)
    {
        monitorRow.Location = new Point(Math.Max(0, (top.Width - monitorRow.Width) / 2), Math.Max(0, (top.Height - monitorRow.Height) / 2));
    }

    void FitRows()
    {
        int w = body.ClientSize.Width - body.Padding.Horizontal - 2;
        foreach (var row in new[] { instanceRow, templateRow, customRow }) row.MaximumSize = new Size(w, 0);
    }

    void BuildMonitors()
    {
        monitorRow.Controls.Clear();
        tiles.Clear();
        foreach (var m in monitors)
        {
            var tile = new MonitorTile(m) { Selected = m.Device == monitorDevice };
            var device = m.Device;
            tile.Click += (s, e) =>
            {
                monitorDevice = device;
                foreach (var t in tiles) t.Selected = t.Monitor.Device == monitorDevice;
            };
            tiles.Add(tile);
            monitorRow.Controls.Add(tile);
        }
    }

    void BuildInstances()
    {
        instanceRow.Controls.Clear();
        foreach (var p in profiles)
        {
            var check = new DarkCheck(p.Name) { Width = Theme.S(180), Checked = ids.Contains(p.Id), Margin = new Padding(Theme.S(6), Theme.S(2), Theme.S(6), Theme.S(2)) };
            var id = p.Id;
            check.Click += (s, e) =>
            {
                if (check.Checked) ids.Add(id); else ids.Remove(id);
                foreach (var c in cards) c.Invalidate();
            };
            instanceRow.Controls.Add(check);
        }
    }

    void BuildCards()
    {
        templateRow.SuspendLayout();
        customRow.SuspendLayout();
        foreach (var c in cards) c.Dispose();
        cards.Clear();
        templateRow.Controls.Clear();
        customRow.Controls.Clear();

        for (int i = 0; i < Layouts.TemplateKeys.Length; i++)
        {
            var key = Layouts.TemplateKeys[i];
            var card = new LayoutCard(key, Layouts.TemplateNames[i], () => Layouts.Template(key, ZoneCount), false);
            AddCard(card, templateRow);
        }

        foreach (var custom in customs)
        {
            var c = custom;
            var card = new LayoutCard(Layouts.CustomPrefix + c.Name, c.Name, () => c.Zones, true);
            card.OnEdit = () => EditCustom(c);
            card.OnDelete = () => DeleteCustom(c);
            AddCard(card, customRow);
        }
        if (customs.Count == 0)
        {
            customRow.Controls.Add(Hint("Belum ada layout custom. Klik \"+ Buat layout baru\" untuk membuat zona sendiri."));
        }

        templateRow.ResumeLayout();
        customRow.ResumeLayout();
        FitRows();
    }

    void AddCard(LayoutCard card, FlowLayoutPanel row)
    {
        card.Selected = card.Key == layoutKey;
        card.OnSelect = () => Select(card.Key);
        cards.Add(card);
        row.Controls.Add(card);
    }

    void Select(string key)
    {
        layoutKey = key;
        foreach (var c in cards) c.Selected = c.Key == layoutKey;
    }

    Func<string, string> NameValidator(ZoneLayout self)
    {
        return name =>
        {
            if (name.Length == 0) return "Nama layout tidak boleh kosong.";
            if (name.Length > 40) return "Maksimal 40 karakter.";
            if (customs.Any(c => c != self && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                return "Nama layout sudah dipakai.";
            return null;
        };
    }

    void NewCustom()
    {
        int n = customs.Count + 1;
        while (customs.Any(c => c.Name == "Custom layout " + n)) n++;
        var layout = new ZoneLayout
        {
            Name = "Custom layout " + n,
            Zones = new List<RectangleF>(Layouts.Resolve(layoutKey, ZoneCount, customs))
        };
        using (var editor = new ZoneEditorForm(layout, WorkSize, NameValidator(null)))
        {
            if (editor.ShowDialog(this) != DialogResult.OK) return;
        }
        customs.Add(layout);
        Layouts.SaveCustom(customs);
        layoutKey = Layouts.CustomPrefix + layout.Name;
        BuildCards();
    }

    void EditCustom(ZoneLayout custom)
    {
        var copy = custom.Clone();
        using (var editor = new ZoneEditorForm(copy, WorkSize, NameValidator(custom)))
        {
            if (editor.ShowDialog(this) != DialogResult.OK) return;
        }
        bool wasSelected = layoutKey == Layouts.CustomPrefix + custom.Name;
        custom.Name = copy.Name;
        custom.Zones = copy.Zones;
        Layouts.SaveCustom(customs);
        if (wasSelected) layoutKey = Layouts.CustomPrefix + custom.Name;
        BuildCards();
    }

    void DeleteCustom(ZoneLayout custom)
    {
        if (!DarkDialog.Confirm(this, "Hapus layout", "Hapus layout \"" + custom.Name + "\"?", "Hapus", true)) return;
        if (layoutKey == Layouts.CustomPrefix + custom.Name) layoutKey = "grid";
        customs.Remove(custom);
        Layouts.SaveCustom(customs);
        BuildCards();
    }

    void Save()
    {
        settings.Monitor = monitorDevice;
        settings.Layout = layoutKey;
        settings.Gap = gapCheck.Checked;
        settings.Ids = profiles.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToList();
        DialogResult = DialogResult.OK;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.Bg);
    }
}

// ---------------------------------------------------------------- zone editor

class ZoneCanvas : Control
{
    public readonly List<RectangleF> Zones;
    readonly SizeF aspect;
    int selected = -1, drag = -1, hoverClose = -1;
    int mode; // 1 left, 2 right, 4 top, 8 bottom, 16 move
    PointF start;
    RectangleF startRect;
    const float SnapDistance = 0.015f, MinSize = 0.05f;

    public ZoneCanvas(List<RectangleF> zones, Size area)
    {
        Zones = new List<RectangleF>(zones);
        aspect = new SizeF(Math.Max(1, area.Width), Math.Max(1, area.Height));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Bg;
    }

    RectangleF Area
    {
        get
        {
            float m = Theme.S(4);
            float s = Math.Min((Width - 2 * m) / aspect.Width, (Height - 2 * m) / aspect.Height);
            float w = aspect.Width * s, h = aspect.Height * s;
            return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        }
    }

    RectangleF ToScreen(RectangleF z)
    {
        var a = Area;
        return new RectangleF(a.X + z.X * a.Width, a.Y + z.Y * a.Height, z.Width * a.Width, z.Height * a.Height);
    }

    PointF ToRel(Point p)
    {
        var a = Area;
        return new PointF((p.X - a.X) / a.Width, (p.Y - a.Y) / a.Height);
    }

    RectangleF CloseRect(RectangleF r)
    {
        int s = Theme.S(26);
        return new RectangleF(r.Right - s - Theme.S(8), r.Y + Theme.S(8), s, s);
    }

    int CloseHit(Point p)
    {
        for (int i = Zones.Count - 1; i >= 0; i--)
        {
            var r = ToScreen(Zones[i]);
            if (r.Width > Theme.S(60) && r.Height > Theme.S(50) && CloseRect(r).Contains(p)) return i;
            if (r.Contains(p)) return -1;
        }
        return -1;
    }

    int HitTest(Point p, out int hitMode)
    {
        hitMode = 0;
        float edge = Theme.S(7);
        for (int i = Zones.Count - 1; i >= 0; i--)
        {
            var r = ToScreen(Zones[i]);
            if (!RectangleF.Inflate(r, edge / 2, edge / 2).Contains(p)) continue;
            if (p.X - r.Left < edge) hitMode |= 1; else if (r.Right - p.X < edge) hitMode |= 2;
            if (p.Y - r.Top < edge) hitMode |= 4; else if (r.Bottom - p.Y < edge) hitMode |= 8;
            if (hitMode == 0) hitMode = 16;
            return i;
        }
        return -1;
    }

    public void AddZone()
    {
        int k = Zones.Count % 5;
        Zones.Add(new RectangleF(0.3f + 0.03f * k, 0.25f + 0.03f * k, 0.4f, 0.5f));
        selected = Zones.Count - 1;
        Invalidate();
    }

    public void RemoveSelected()
    {
        if (selected < 0 || selected >= Zones.Count) return;
        Zones.RemoveAt(selected);
        selected = -1;
        Invalidate();
    }

    public void Reset(List<RectangleF> zones)
    {
        Zones.Clear();
        Zones.AddRange(zones);
        selected = -1;
        Invalidate();
    }

    float Snap(float v, int self, bool horizontal)
    {
        var candidates = new List<float> { 0, 1 };
        for (int j = 0; j < Zones.Count; j++)
        {
            if (j == self) continue;
            var z = Zones[j];
            if (horizontal) { candidates.Add(z.Left); candidates.Add(z.Right); }
            else { candidates.Add(z.Top); candidates.Add(z.Bottom); }
        }
        foreach (var c in candidates)
        {
            if (Math.Abs(v - c) < SnapDistance) return c;
        }
        return (float)Math.Round(v * 100) / 100;
    }

    static float Clamp(float v, float min, float max)
    {
        return Math.Max(min, Math.Min(max, v));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        int close = CloseHit(e.Location);
        if (close >= 0)
        {
            Zones.RemoveAt(close);
            selected = -1;
            hoverClose = -1;
            Invalidate();
            return;
        }
        int hit;
        selected = HitTest(e.Location, out hit);
        if (selected >= 0)
        {
            drag = selected;
            mode = hit;
            start = ToRel(e.Location);
            startRect = Zones[selected];
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag < 0)
        {
            int close = CloseHit(e.Location);
            if (close != hoverClose) { hoverClose = close; Invalidate(); }
            int hit;
            int i = HitTest(e.Location, out hit);
            if (close >= 0) Cursor = Cursors.Hand;
            else if (i < 0) Cursor = Cursors.Default;
            else if (hit == (1 | 4) || hit == (2 | 8)) Cursor = Cursors.SizeNWSE;
            else if (hit == (2 | 4) || hit == (1 | 8)) Cursor = Cursors.SizeNESW;
            else if (hit == 1 || hit == 2) Cursor = Cursors.SizeWE;
            else if (hit == 4 || hit == 8) Cursor = Cursors.SizeNS;
            else Cursor = Cursors.SizeAll;
            return;
        }

        var p = ToRel(e.Location);
        float dx = p.X - start.X, dy = p.Y - start.Y;
        var r = startRect;
        float l = r.Left, t = r.Top, rr = r.Right, b = r.Bottom;
        if (mode == 16)
        {
            float w = r.Width, h = r.Height;
            l = Clamp(l + dx, 0, 1 - w);
            t = Clamp(t + dy, 0, 1 - h);
            float byLeft = Snap(l, drag, true), byRight = Snap(l + w, drag, true) - w;
            l = Clamp(Math.Abs(byLeft - l) <= Math.Abs(byRight - l) ? byLeft : byRight, 0, 1 - w);
            float byTop = Snap(t, drag, false), byBottom = Snap(t + h, drag, false) - h;
            t = Clamp(Math.Abs(byTop - t) <= Math.Abs(byBottom - t) ? byTop : byBottom, 0, 1 - h);
            rr = l + w;
            b = t + h;
        }
        else
        {
            if ((mode & 1) != 0) l = Math.Min(Snap(Clamp(l + dx, 0, 1), drag, true), rr - MinSize);
            if ((mode & 2) != 0) rr = Math.Max(Snap(Clamp(rr + dx, 0, 1), drag, true), l + MinSize);
            if ((mode & 4) != 0) t = Math.Min(Snap(Clamp(t + dy, 0, 1), drag, false), b - MinSize);
            if ((mode & 8) != 0) b = Math.Max(Snap(Clamp(b + dy, 0, 1), drag, false), t + MinSize);
        }
        Zones[drag] = RectangleF.FromLTRB(l, t, rr, b);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        drag = -1;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverClose >= 0) { hoverClose = -1; Invalidate(); }
    }

    // Double-click splits a zone in two: left/right, or top/bottom with Shift.
    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        int hit;
        int i = HitTest(PointToClient(Cursor.Position), out hit);
        if (i < 0) return;
        var z = Zones[i];
        RectangleF a, b;
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            if (z.Height / 2 < MinSize) return;
            a = new RectangleF(z.X, z.Y, z.Width, z.Height / 2);
            b = new RectangleF(z.X, z.Y + z.Height / 2, z.Width, z.Height / 2);
        }
        else
        {
            if (z.Width / 2 < MinSize) return;
            a = new RectangleF(z.X, z.Y, z.Width / 2, z.Height);
            b = new RectangleF(z.X + z.Width / 2, z.Y, z.Width / 2, z.Height);
        }
        Zones[i] = a;
        Zones.Insert(i + 1, b);
        selected = i + 1;
        drag = -1;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Delete) RemoveSelected();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var a = Area;
        using (var b = new SolidBrush(EditorStyle.Canvas)) g.FillRectangle(b, a);
        using (var pen = new Pen(EditorStyle.GridLine))
        {
            for (int i = 1; i < 10; i++)
            {
                g.DrawLine(pen, a.X + a.Width * i / 10, a.Y, a.X + a.Width * i / 10, a.Bottom);
                g.DrawLine(pen, a.X, a.Y + a.Height * i / 10, a.Right, a.Y + a.Height * i / 10);
            }
        }
        using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, a.X, a.Y, a.Width, a.Height);

        var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        for (int i = 0; i < Zones.Count; i++)
        {
            var z = Zones[i];
            var r = ToScreen(z);
            r.Inflate(-1.5f, -1.5f);
            bool isSelected = i == selected;
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, r);
            if (isSelected)
            {
                using (var b = new SolidBrush(Color.FromArgb(16, Theme.Accent))) g.FillRectangle(b, r);
            }
            using (var pen = new Pen(isSelected ? Theme.Accent : Theme.BorderHover, isSelected ? 2 : 1))
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

            var ri = Rectangle.Round(r);
            if (r.Width > Theme.S(60) && r.Height > Theme.S(70))
            {
                int numH = Theme.S(44);
                var numRect = new Rectangle(ri.X, ri.Y + ri.Height / 2 - numH, ri.Width, numH);
                TextRenderer.DrawText(g, (i + 1).ToString(), EditorStyle.ZoneNumber, numRect, isSelected ? Theme.Accent : Theme.Text, center);
                var size = (int)Math.Round(z.Width * aspect.Width) + " × " + (int)Math.Round(z.Height * aspect.Height) +
                           "   (" + Math.Round(z.Width * 100) + "% × " + Math.Round(z.Height * 100) + "%)";
                TextRenderer.DrawText(g, size, Theme.Small, new Rectangle(ri.X, ri.Y + ri.Height / 2 + Theme.S(4), ri.Width, Theme.S(20)), Theme.Muted, center | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Name, ri, Theme.Text, center);
            }

            if (r.Width > Theme.S(60) && r.Height > Theme.S(50))
            {
                var cr = CloseRect(r);
                using (var path = Theme.Round(cr, cr.Height / 2))
                using (var b = new SolidBrush(i == hoverClose ? Theme.Danger : Theme.SurfaceHover))
                    g.FillPath(b, path);
                TextRenderer.DrawText(g, EditorStyle.CloseGlyph, EditorStyle.Icon, Rectangle.Round(cr),
                    i == hoverClose ? Color.White : Theme.Muted, center);
            }
        }
    }
}

class ZoneEditorForm : Form
{
    readonly ZoneLayout layout;
    readonly ZoneCanvas canvas;
    readonly TextBox nameInput;
    readonly Label hint;
    readonly Func<string, string> validate;
    const string HintText = "Geser zona untuk memindah  ·  tarik tepi/sudut untuk ubah ukuran  ·  klik dua kali untuk membagi (Shift: atas/bawah)  ·  Delete untuk hapus";

    public ZoneEditorForm(ZoneLayout layout, Size area, Func<string, string> validate)
    {
        this.layout = layout;
        this.validate = validate;
        Text = "Editor zona";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ClientSize = new Size(Theme.S(1000), Theme.S(700));
        MinimumSize = new Size(Theme.S(720), Theme.S(500));

        // toolbar
        var top = new Panel { Dock = DockStyle.Top, Height = Theme.S(68), BackColor = Theme.Bg };
        var nameBox = new Panel { Location = new Point(Theme.S(20), Theme.S(16)), Size = new Size(Theme.S(300), Theme.S(38)), BackColor = Theme.Surface };
        nameInput = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Input, Text = layout.Name };
        nameInput.Location = new Point(Theme.S(10), (nameBox.Height - nameInput.PreferredHeight) / 2);
        nameInput.Width = nameBox.Width - Theme.S(20);
        nameBox.Controls.Add(nameInput);
        nameBox.Paint += (s, e) =>
        {
            using (var pen = new Pen(nameInput.Focused ? Theme.BorderHover : Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, nameBox.Width - 1, nameBox.Height - 1);
        };
        nameInput.GotFocus += (s, e) => nameBox.Invalidate();
        nameInput.LostFocus += (s, e) => nameBox.Invalidate();

        canvas = new ZoneCanvas(layout.Zones, area) { Dock = DockStyle.Fill };

        var addBtn = ToolButton("+  Zona", Theme.S(96), () => canvas.AddZone());
        var delBtn = ToolButton("Hapus zona", Theme.S(116), () => canvas.RemoveSelected());
        var colBtn = ToolButton("Kolom", Theme.S(84), () => canvas.Reset(Layouts.Template("columns", Math.Max(2, canvas.Zones.Count))));
        var gridBtn = ToolButton("Grid", Theme.S(76), () => canvas.Reset(Layouts.Template("grid", Math.Max(2, canvas.Zones.Count))));
        var tools = new[] { gridBtn, colBtn, delBtn, addBtn };
        top.Controls.Add(nameBox);
        top.Controls.AddRange(tools);
        top.Resize += (s, e) =>
        {
            int x = top.Width - Theme.S(20);
            foreach (var b in tools)
            {
                x -= b.Width;
                b.Location = new Point(x, Theme.S(17));
                x -= Theme.S(8);
            }
        };

        // footer
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(68), BackColor = Theme.Bg };
        hint = new Label { Text = HintText, ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        var cancelBtn = FlatButton.Secondary("Batal");
        cancelBtn.Size = new Size(Theme.S(100), Theme.S(36));
        cancelBtn.Click += (s, e) => DialogResult = DialogResult.Cancel;
        var saveBtn = FlatButton.Primary("Simpan");
        saveBtn.Size = new Size(Theme.S(100), Theme.S(36));
        saveBtn.Click += (s, e) => Save();
        cancelBtn.BackColor = Theme.Bg;
        saveBtn.BackColor = Theme.Bg;
        bottom.Controls.AddRange(new Control[] { hint, cancelBtn, saveBtn });
        bottom.Resize += (s, e) =>
        {
            int pad = Theme.S(20), y = (bottom.Height - Theme.S(36)) / 2;
            saveBtn.Location = new Point(bottom.Width - pad - saveBtn.Width, y);
            cancelBtn.Location = new Point(saveBtn.Left - Theme.S(8) - cancelBtn.Width, y);
            hint.Bounds = new Rectangle(pad, 0, Math.Max(10, cancelBtn.Left - 2 * pad), bottom.Height);
        };

        var canvasHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(20), Theme.S(4), Theme.S(20), Theme.S(4)), BackColor = Theme.Bg };
        canvasHost.Controls.Add(canvas);

        Controls.Add(canvasHost);
        Controls.Add(bottom);
        Controls.Add(top);
    }

    FlatButton ToolButton(string text, int width, Action onClick)
    {
        var b = FlatButton.Secondary(text);
        b.BackColor = Theme.Bg;
        b.Size = new Size(width, Theme.S(36));
        b.Click += (s, e) => onClick();
        return b;
    }

    void Save()
    {
        var name = nameInput.Text.Trim();
        var error = validate(name);
        if (error == null && canvas.Zones.Count == 0) error = "Minimal harus ada satu zona.";
        if (error != null)
        {
            hint.ForeColor = Theme.Danger;
            hint.Text = error;
            return;
        }
        layout.Name = name;
        layout.Zones = new List<RectangleF>(canvas.Zones);
        DialogResult = DialogResult.OK;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.Bg);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        canvas.Focus();
    }
}
