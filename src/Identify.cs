// "Identify" badge: a glass label centred on a display for ~2 s, then fading out.
// Click-through and never activated, so the panel stays open and usable meanwhile.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

class IdentifyOverlay : Form
{
    static readonly Dictionary<Display, IdentifyOverlay> open = new Dictionary<Display, IdentifyOverlay>();

    public static void ShowFor(IEnumerable<Display> displays)
    {
        foreach (var d in displays)
        {
            if (d.Bounds.IsEmpty) continue;
            IdentifyOverlay o;
            if (!open.TryGetValue(d, out o) || o.IsDisposed) { o = new IdentifyOverlay(d); open[d] = o; }
            o.Present();
        }
    }

    const int HoldMs = 2000, FadeMs = 400;
    readonly Display d;
    readonly float k;
    readonly Font fName = new Font("Segoe UI Semibold", 26f), fInfo = new Font("Segoe UI", 13f);
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 };
    DateTime until;
    Bitmap bmp;

    IdentifyOverlay(Display d)
    {
        this.d = d;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
        timer.Tick += (s, e) => Tick();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // layered, tool window, click-through, topmost, never takes focus
            cp.ExStyle |= 0x80000 | 0x80 | 0x20 | 0x8 | 0x08000000;
            return cp;
        }
    }

    void Present()
    {
        until = DateTime.Now.AddMilliseconds(HoldMs);
        if (bmp != null) bmp.Dispose();
        bmp = Draw();
        var b = d.Bounds;
        SetBounds(b.X + (b.Width - bmp.Width) / 2, b.Y + (b.Height - bmp.Height) / 2, bmp.Width, bmp.Height);
        if (!Visible) Show();
        Glass.Push(this, bmp, 255);
        timer.Start();
    }

    void Tick()
    {
        double over = (DateTime.Now - until).TotalMilliseconds;
        if (over < 0) return;
        if (over >= FadeMs) { timer.Stop(); Hide(); return; }
        Glass.Push(this, bmp, (byte)Math.Round(255 * (1 - over / FadeMs)));
    }

    Bitmap Draw()
    {
        string info = d.Percent + " %" + (d.Primary ? "  ·  " + L.PrimaryTag : "");
        SizeF sn, si;
        using (var tmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(tmp)) { sn = g.MeasureString(d.Name, fName); si = g.MeasureString(info, fInfo); }

        int w = (int)Math.Max(320 * k, Math.Max(sn.Width, si.Width) + 72 * k);
        int h = (int)(sn.Height + si.Height + 52 * k);
        var bmpOut = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmpOut))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var p = Glass.Round(new RectangleF(0, 0, w - 1, h - 1), 18 * k))
            {
                using (var b = new SolidBrush(Color.FromArgb(215, 30, 30, 34))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1)) g.DrawPath(pen, p);
            }
            var sf = new StringFormat { Alignment = StringAlignment.Center };
            float y = 22 * k;
            g.DrawString(d.Name, fName, Brushes.White, new RectangleF(0, y, w, sn.Height), sf);
            using (var b = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
                g.DrawString(info, fInfo, b, new RectangleF(0, y + sn.Height + 4 * k, w, si.Height), sf);
        }
        return bmpOut;
    }
}
