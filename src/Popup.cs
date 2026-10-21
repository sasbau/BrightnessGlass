// Glass popup in the style of macOS MonitorControl: acrylic blur behind a per-pixel-alpha
// layered window, rounded cards, pill sliders with white fill and round knob. Fully custom-drawn.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Glass
{
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState, AccentFlags, GradientColor, AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int W, H; public SIZE(int w, int h) { W = w; H = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    // acrylic blur behind the window (Win10 1803+ / Win11); tint comes from our own pixels
    public static void EnableBlur(IntPtr hwnd, int width, int height, int radius)
    {
        var accent = new AccentPolicy { AccentState = 4 /* ACRYLICBLURBEHIND */, AccentFlags = 2, GradientColor = 0x01101010 };
        int size = Marshal.SizeOf(accent);
        IntPtr p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, p, false);
            var data = new WinCompAttrData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = p, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(p); }
        int round = 2; // DWMWCP_ROUND on Win11
        DwmSetWindowAttribute(hwnd, 33, ref round, 4);
        SetWindowRgn(hwnd, CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2), true);
    }

    // put a premultiplied 32-bit bitmap on a WS_EX_LAYERED window with the given overall opacity
    public static void Push(Form f, Bitmap bmp, byte alpha)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hbmp);
        try
        {
            var size = new SIZE(bmp.Width, bmp.Height);
            var src = new POINT(0, 0);
            var dst = new POINT(f.Left, f.Top);
            var blend = new BLENDFUNCTION { Op = 0, Flags = 0, Alpha = alpha, Format = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(f.Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

class GlassPopup : Form
{
    class Slider { public RectangleF Track, Tile; public int Value; public Display Disp; } // Disp == null -> master
    class Item { public RectangleF Rect; public string Text, Hint; public Action Click; }

    readonly Controller ctl;
    readonly Action openSettings, quit;
    readonly Func<string> presetHint;
    readonly float k; // DPI scale
    readonly Font fTitle, fText, fSmall, fValue;
    readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 80 };
    readonly Dictionary<Display, int> pendingSingle = new Dictionary<Display, int>();
    int pendingMaster = -1;

    List<Slider> sliders = new List<Slider>();
    List<Item> items = new List<Item>();
    RectangleF toggleRect;
    string status = "";
    Slider dragging, hoverTile;
    Item hover;
    int W, H;
    readonly ToolTip tip = new ToolTip();

    public GlassPopup(Controller ctl, Action openSettings, Action quit, Func<string> presetHint)
    {
        this.ctl = ctl; this.openSettings = openSettings; this.quit = quit; this.presetHint = presetHint;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
        string fam = FontFamily.Families.Length > 0 && Array.Exists(FontFamily.Families, f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        fTitle = new Font("Segoe UI Semibold", 10.5f);
        fText = new Font(fam, 10.5f);
        fSmall = new Font(fam, 8.75f);
        fValue = new Font(fam, 10.5f);

        debounce.Tick += (s, e) => Flush();
        Deactivate += (s, e) => { dragging = null; Hide(); };
        KeyDown += OnKey;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 /* WS_EX_LAYERED */ | 0x80 /* WS_EX_TOOLWINDOW */;
            return cp;
        }
    }

    float S(float v) { return v * k; }

    // ---------------------------------------------------------------- layout
    void Layout_()
    {
        bool tiles = ctl.Displays.Exists(d => !d.Bounds.IsEmpty);
        W = (int)S(tiles ? 400 : 360);
        float pad = S(12), cp = S(16), y = pad, cardW = W - 2 * pad;
        sliders.Clear(); items.Clear();

        // card 1: auto toggle + status
        y += S(64) + S(10);
        toggleRect = new RectangleF(W - pad - cp - S(40), pad + S(14), S(40), S(24));

        // card 2: master ("All"); arrangement tile in its own column right of the percentage
        float tileW = tiles ? S(84) : 0, tileGap = tiles ? S(12) : 0;
        float trackW = cardW - 2 * cp - S(52) - tileW - tileGap;
        float tileX = pad + cardW - cp - tileW;
        sliders.Add(MakeSlider(new RectangleF(pad + cp, y + S(42), trackW, S(28)), ctl.S.Master, null, tileX, tileW));
        y += S(88) + S(10);

        // card 3: one row per display
        float y0 = y + S(14);
        foreach (var d in ctl.Displays)
        {
            sliders.Add(MakeSlider(new RectangleF(pad + cp, y0 + S(26), trackW, S(28)), d.Percent, d, tileX, tileW));
            y0 += S(70);
        }
        if (ctl.Displays.Count > 0) y = y0 + S(4) + S(8);

        // menu
        items.Add(new Item { Rect = new RectangleF(pad, y, cardW, S(30)), Text = L.MenuSettings, Hint = presetHint(), Click = () => { Hide(); openSettings(); } });
        y += S(30);
        items.Add(new Item { Rect = new RectangleF(pad, y, cardW, S(30)), Text = L.MenuQuit, Click = () => { Hide(); quit(); } });
        y += S(30) + pad - S(4);
        H = (int)y;
    }

    Slider MakeSlider(RectangleF track, int value, Display d, float tileX, float tileW)
    {
        var sl = new Slider { Track = track, Value = value, Disp = d };
        if (tileW > 0 && (d == null || !d.Bounds.IsEmpty))
        {
            float top = track.Y - S(27), center = (top + track.Bottom) / 2;
            sl.Tile = new RectangleF(tileX, center - S(23), tileW, S(46));
        }
        return sl;
    }

    // ---------------------------------------------------------------- render
    void Render()
    {
        if (W == 0) return;
        using (var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                float pad = S(12), cp = S(16), cardW = W - 2 * pad;

                using (var path = Glass.Round(new RectangleF(0, 0, W - 1, H - 1), S(12)))
                {
                    using (var b = new SolidBrush(Color.FromArgb(70, 24, 24, 28))) g.FillPath(b, path);
                    using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1)) g.DrawPath(pen, path);
                }

                // card 1: auto
                float y = pad;
                Card(g, new RectangleF(pad, y, cardW, S(64)));
                g.DrawString(L.AutoTitle, fTitle, Brushes.White, pad + cp, y + S(12));
                using (var b = new SolidBrush(Color.FromArgb(170, 255, 255, 255))) g.DrawString(status, fSmall, b, pad + cp, y + S(36));
                Toggle(g, toggleRect, ctl.S.Auto);
                y += S(64) + S(10);

                // card 2: master
                Card(g, new RectangleF(pad, y, cardW, S(88)));
                g.DrawString(L.AllDisplays, fTitle, Brushes.White, pad + cp, y + S(13));
                DrawSlider(g, sliders[0]);
                DrawTile(g, sliders[0]);
                y += S(88) + S(10);

                // card 3: displays
                if (ctl.Displays.Count > 0)
                {
                    float h = ctl.Displays.Count * S(70) + S(4) + S(14) - S(10);
                    Card(g, new RectangleF(pad, y, cardW, h));
                    for (int i = 1; i < sliders.Count; i++)
                    {
                        var sl = sliders[i];
                        float ny = sl.Track.Y - S(25);
                        using (var b = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
                            g.DrawString(sl.Disp.Name, fTitle, b, pad + cp, ny);
                        if (sl.Disp.Primary) DrawTag(g, L.PrimaryTag, pad + cp + g.MeasureString(sl.Disp.Name, fTitle).Width + S(4), ny + S(1));
                        DrawSlider(g, sl);
                        DrawTile(g, sl);
                    }
                }

                // menu items
                foreach (var it in items)
                {
                    if (it == hover)
                        using (var b = new SolidBrush(Color.FromArgb(38, 255, 255, 255)))
                        using (var p = Glass.Round(it.Rect, S(6))) g.FillPath(b, p);
                    var sf = new StringFormat { LineAlignment = StringAlignment.Center };
                    g.DrawString(it.Text, fText, Brushes.White, new RectangleF(it.Rect.X + S(8), it.Rect.Y, it.Rect.Width, it.Rect.Height), sf);
                    if (!string.IsNullOrEmpty(it.Hint))
                    {
                        sf.Alignment = StringAlignment.Far;
                        using (var b = new SolidBrush(Color.FromArgb(130, 255, 255, 255)))
                            g.DrawString(it.Hint, fSmall, b, new RectangleF(it.Rect.X, it.Rect.Y, it.Rect.Width - S(8), it.Rect.Height), sf);
                    }
                }
            }
            Push(bmp);
        }
    }

    void Card(Graphics g, RectangleF r)
    {
        using (var p = Glass.Round(r, S(12)))
        {
            using (var b = new SolidBrush(Color.FromArgb(22, 255, 255, 255))) g.FillPath(b, p);
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1)) g.DrawPath(pen, p);
        }
    }

    void DrawTag(Graphics g, string text, float x, float y)
    {
        var sz = g.MeasureString(text, fSmall);
        var r = new RectangleF(x, y, sz.Width + S(8), S(19));
        using (var p = Glass.Round(r, S(6)))
        using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillPath(b, p);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using (var b = new SolidBrush(Color.FromArgb(230, 255, 255, 255))) g.DrawString(text, fSmall, b, r, sf);
    }

    // miniature of the Windows arrangement; this display filled, the others outlined (master: all filled)
    void DrawTile(Graphics g, Slider sl)
    {
        if (sl.Tile.IsEmpty) return;
        using (var p = Glass.Round(sl.Tile, S(10)))
        using (var b = new SolidBrush(Color.FromArgb(sl == hoverTile ? 60 : 28, 255, 255, 255))) g.FillPath(b, p);

        var known = ctl.Displays.FindAll(d => !d.Bounds.IsEmpty);
        if (known.Count == 0) return;
        var u = known[0].Bounds;
        foreach (var d in known) u = Rectangle.Union(u, d.Bounds);

        var inner = RectangleF.Inflate(sl.Tile, -S(8), -S(9));
        float stand = S(4);
        float sc = Math.Min(inner.Width / u.Width, (inner.Height - stand) / u.Height);
        float ox = inner.X + (inner.Width - u.Width * sc) / 2, oy = inner.Y + (inner.Height - stand - u.Height * sc) / 2;

        using (var fill = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
        using (var dim = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
        using (var pen = new Pen(Color.FromArgb(140, 255, 255, 255), Math.Max(1, S(1))))
        {
            foreach (var d in known)
            {
                var r = new RectangleF(ox + (d.Bounds.X - u.X) * sc, oy + (d.Bounds.Y - u.Y) * sc, d.Bounds.Width * sc, d.Bounds.Height * sc);
                r.Inflate(-S(1), -S(1));
                bool on = sl.Disp == null || sl.Disp == d;
                using (var path = Glass.Round(r, S(2)))
                {
                    if (on) g.FillPath(fill, path); else g.DrawPath(pen, path);
                }
                // stand only if no display sits directly below (stacked layouts)
                bool below = known.Exists(o => o != d && o.Bounds.Top >= d.Bounds.Bottom - 1 && o.Bounds.Top - d.Bounds.Bottom < d.Bounds.Height / 4
                                               && o.Bounds.Left < d.Bounds.Right && o.Bounds.Right > d.Bounds.Left);
                float sw = Math.Max(S(3), r.Width * 0.16f);
                if (!below) g.FillRectangle(on ? fill : dim, r.X + (r.Width - sw) / 2, r.Bottom + S(1), sw, S(2));
            }
        }
    }

    void Toggle(Graphics g, RectangleF r, bool on)
    {
        using (var p = Glass.Round(r, r.Height / 2))
        using (var b = new SolidBrush(on ? Color.FromArgb(255, 52, 199, 89) : Color.FromArgb(70, 255, 255, 255)))
            g.FillPath(b, p);
        float d = r.Height - S(4);
        float x = on ? r.Right - d - S(2) : r.X + S(2);
        using (var sh = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(sh, x, r.Y + S(2.5f), d, d);
        g.FillEllipse(Brushes.White, x, r.Y + S(2), d, d);
    }

    void DrawSlider(Graphics g, Slider sl)
    {
        var t = sl.Track;
        float rad = t.Height / 2;
        using (var p = Glass.Round(t, rad))
        using (var b = new SolidBrush(Color.FromArgb(45, 255, 255, 255))) g.FillPath(b, p);

        float cx = KnobX(sl);
        using (var p = Glass.Round(new RectangleF(t.X, t.Y, cx + rad - t.X, t.Height), rad))
        using (var b = new SolidBrush(Color.FromArgb(240, 255, 255, 255))) g.FillPath(b, p);

        // sun glyph at the left end
        float sx = t.X + rad, sy = t.Y + rad;
        using (var pen = new Pen(Color.FromArgb(200, 70, 70, 74), S(1.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawEllipse(pen, sx - S(3.5f), sy - S(3.5f), S(7), S(7));
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                g.DrawLine(pen, sx + (float)Math.Cos(a) * S(6), sy + (float)Math.Sin(a) * S(6), sx + (float)Math.Cos(a) * S(8), sy + (float)Math.Sin(a) * S(8));
            }
        }

        // knob with soft shadow
        using (var sh = new SolidBrush(Color.FromArgb(45, 0, 0, 0))) g.FillEllipse(sh, cx - rad, t.Y + S(1), t.Height, t.Height);
        using (var pen = new Pen(Color.FromArgb(35, 0, 0, 0), 1)) { g.FillEllipse(Brushes.White, cx - rad, t.Y, t.Height, t.Height); g.DrawEllipse(pen, cx - rad, t.Y, t.Height, t.Height); }

        var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            g.DrawString(sl.Value + "%", fValue, b, new RectangleF(t.Right, t.Y, S(52), t.Height), sf);
    }

    float KnobX(Slider sl)
    {
        float rad = sl.Track.Height / 2;
        return sl.Track.X + rad + (sl.Track.Width - 2 * rad) * sl.Value / 100f;
    }

    int ValueAt(Slider sl, float x)
    {
        float rad = sl.Track.Height / 2;
        double v = (x - sl.Track.X - rad) / (sl.Track.Width - 2 * rad) * 100;
        return (int)Math.Round(Math.Max(0, Math.Min(100, v)));
    }

    void Push(Bitmap bmp) { Glass.Push(this, bmp, 255); }

    // ---------------------------------------------------------------- input
    Slider SliderAt(Point p)
    {
        foreach (var sl in sliders)
        {
            var r = sl.Track; r.Inflate(S(4), S(6));
            if (r.Contains(p)) return sl;
        }
        return null;
    }

    Slider TileAt(Point p)
    {
        foreach (var sl in sliders) if (!sl.Tile.IsEmpty && sl.Tile.Contains(p)) return sl;
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (toggleRect.Contains(e.Location)) { SetAuto(!ctl.S.Auto); return; }
        var tile = TileAt(e.Location);
        if (tile != null)
        {
            IdentifyOverlay.ShowFor(tile.Disp == null ? ctl.Displays : new List<Display> { tile.Disp });
            return;
        }
        foreach (var it in items) if (it.Rect.Contains(e.Location)) { it.Click(); return; }
        dragging = SliderAt(e.Location);
        if (dragging != null) Change(dragging, ValueAt(dragging, e.X));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging != null) { Change(dragging, ValueAt(dragging, e.X)); return; }
        Item h = null;
        foreach (var it in items) if (it.Rect.Contains(e.Location)) h = it;
        var ht = TileAt(e.Location);
        Cursor = h != null || ht != null || toggleRect.Contains(e.Location) || SliderAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
        if (ht != hoverTile)
        {
            if (ht != null) tip.Show(L.Identify, this, (int)ht.Tile.X, (int)(ht.Tile.Bottom + S(4)), 1500);
            else tip.Hide(this);
        }
        if (h != hover || ht != hoverTile) { hover = h; hoverTile = ht; Render(); }
    }

    protected override void OnMouseUp(MouseEventArgs e) { dragging = null; }
    protected override void OnMouseLeave(EventArgs e)
    {
        tip.Hide(this);
        if (hover != null || hoverTile != null) { hover = null; hoverTile = null; Render(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var sl = SliderAt(PointToClient(Cursor.Position)) ?? sliders[0];
        Change(sl, sl.Value + (e.Delta > 0 ? 5 : -5));
    }

    void OnKey(object s, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Hide();
        else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) Change(sliders[0], sliders[0].Value + 5);
        else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) Change(sliders[0], sliders[0].Value - 5);
    }

    void Change(Slider sl, int v)
    {
        v = Math.Max(0, Math.Min(100, v));
        if (sl.Value == v) return;
        sl.Value = v;
        if (sl.Disp == null)
        {
            pendingMaster = v;
            for (int i = 1; i < sliders.Count; i++) sliders[i].Value = ctl.S.Map(sliders[i].Disp, v);
        }
        else pendingSingle[sl.Disp] = v;
        Render();
        debounce.Stop(); debounce.Start();
    }

    void Flush()
    {
        debounce.Stop();
        if (pendingMaster >= 0)
        {
            ctl.LearnMaster(pendingMaster);
            ctl.SetMaster(pendingMaster);
            pendingMaster = -1;
        }
        foreach (var kv in pendingSingle) ctl.SetSingle(kv.Key, kv.Value);
        pendingSingle.Clear();
    }

    void SetAuto(bool on)
    {
        ctl.S.Auto = on; ctl.S.Save(); ctl.Invalidate(); ctl.ApplyAuto(true);
        RefreshValues();
    }

    // called when values changed elsewhere (auto, hotkeys)
    public void RefreshValues()
    {
        if (!Visible) return;
        status = ctl.Status();
        sliders[0].Value = ctl.S.Master;
        for (int i = 1; i < sliders.Count; i++) sliders[i].Value = ctl.S.Map(sliders[i].Disp, ctl.S.Master);
        Render();
    }

    public void Toggle()
    {
        if (Visible) { Hide(); return; }
        foreach (var d in ctl.Displays) d.Read(); // may have been changed via OSD buttons
        status = ctl.Status();
        Layout_();
        var wa = Screen.PrimaryScreen.WorkingArea;
        SetBounds(wa.Right - W - (int)S(12), wa.Bottom - H - (int)S(12), W, H);
        Show();
        Glass.EnableBlur(Handle, W, H, (int)S(12));
        Render();
        Activate();
    }
}
