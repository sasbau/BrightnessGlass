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
    class Slider { public RectangleF Track; public int Value; public Display Disp; } // Disp == null -> master
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
    Slider dragging;
    Item hover;
    int W, H;

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
        W = (int)S(360);
        float pad = S(12), cp = S(16), y = pad, cardW = W - 2 * pad;
        sliders.Clear(); items.Clear();

        // card 1: auto toggle + status
        y += S(64) + S(10);
        toggleRect = new RectangleF(W - pad - cp - S(40), pad + S(14), S(40), S(24));

        // card 2: master ("All")
        float trackW = cardW - 2 * cp - S(52);
        sliders.Add(new Slider { Track = new RectangleF(pad + cp, y + S(42), trackW, S(28)), Value = ctl.S.Master });
        y += S(88) + S(10);

        // card 3: one row per display
        float y0 = y + S(14);
        foreach (var d in ctl.Displays)
        {
            sliders.Add(new Slider { Track = new RectangleF(pad + cp, y0 + S(26), trackW, S(28)), Value = d.Percent, Disp = d });
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
                y += S(88) + S(10);

                // card 3: displays
                if (ctl.Displays.Count > 0)
                {
                    float h = ctl.Displays.Count * S(70) + S(4) + S(14) - S(10);
                    Card(g, new RectangleF(pad, y, cardW, h));
                    for (int i = 1; i < sliders.Count; i++)
                    {
                        var sl = sliders[i];
                        using (var b = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
                            g.DrawString(sl.Disp.Name, fTitle, b, pad + cp, sl.Track.Y - S(25));
                        DrawSlider(g, sl);
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

    void Push(Bitmap bmp)
    {
        IntPtr screen = Glass.GetDC(IntPtr.Zero), mem = Glass.CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = Glass.SelectObject(mem, hbmp);
        try
        {
            var size = new Glass.SIZE(bmp.Width, bmp.Height);
            var src = new Glass.POINT(0, 0);
            var dst = new Glass.POINT(Left, Top);
            var blend = new Glass.BLENDFUNCTION { Op = 0, Flags = 0, Alpha = 255, Format = 1 /* AC_SRC_ALPHA */ };
            Glass.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            Glass.SelectObject(mem, old);
            Glass.DeleteObject(hbmp);
            Glass.DeleteDC(mem);
            Glass.ReleaseDC(IntPtr.Zero, screen);
        }
    }

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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (toggleRect.Contains(e.Location)) { SetAuto(!ctl.S.Auto); return; }
        foreach (var it in items) if (it.Rect.Contains(e.Location)) { it.Click(); return; }
        dragging = SliderAt(e.Location);
        if (dragging != null) Change(dragging, ValueAt(dragging, e.X));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging != null) { Change(dragging, ValueAt(dragging, e.X)); return; }
        Item h = null;
        foreach (var it in items) if (it.Rect.Contains(e.Location)) h = it;
        Cursor = h != null || toggleRect.Contains(e.Location) || SliderAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
        if (h != hover) { hover = h; Render(); }
    }

    protected override void OnMouseUp(MouseEventArgs e) { dragging = null; }
    protected override void OnMouseLeave(EventArgs e) { if (hover != null) { hover = null; Render(); } }

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
