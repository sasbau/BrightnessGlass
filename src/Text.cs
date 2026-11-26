// Crisp text on the glass popup.
// The popup is a per-pixel-alpha layered window, where ClearType cannot work (no known background
// colour per pixel) and GDI+'s own grayscale text comes out blurry: stems are spread over two pixels.
// Instead, each string is rasterised by GDI with ClearType hinting (white on black), the three
// subpixel channels are averaged into one coverage value, and that is used as alpha. The result is
// grayscale anti-aliasing with Windows' own hinting: stems on whole pixels, normal stroke weight.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class TextLayer
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    class LOGFONT
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontIndirect(LOGFONT lf);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr dc, int c);
    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int m);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOut(IntPtr dc, int x, int y, string s, int n);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32(IntPtr dc, string s, int n, out Size size);

    const byte CLEARTYPE_QUALITY = 5;

    struct Op { public string Text; public Font Font; public Color Color; public int X, Y; }
    readonly List<Op> ops = new List<Op>();

    // size of the text cell (no padding), in pixels at 96 dpi like the popup bitmap
    public static Size Measure(string s, Font f)
    {
        if (string.IsNullOrEmpty(s)) return Size.Empty;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr hf = CreateFont(f), dc = g.GetHdc(), old = SelectObject(dc, hf);
            try { Size sz; GetTextExtentPoint32(dc, s, s.Length, out sz); return sz; }
            finally { SelectObject(dc, old); DeleteObject(hf); g.ReleaseHdc(dc); }
        }
    }

    // top-left of the text cell at (x, y)
    public void Draw(string s, Font f, Color c, float x, float y)
    {
        if (!string.IsNullOrEmpty(s)) ops.Add(new Op { Text = s, Font = f, Color = c, X = (int)Math.Round(x), Y = (int)Math.Round(y) });
    }

    // aligned inside a rectangle
    public void Draw(string s, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
    {
        if (string.IsNullOrEmpty(s)) return;
        var sz = Measure(s, f);
        float x = h == StringAlignment.Near ? r.X : h == StringAlignment.Center ? r.X + (r.Width - sz.Width) / 2 : r.Right - sz.Width;
        float y = v == StringAlignment.Near ? r.Y : v == StringAlignment.Center ? r.Y + (r.Height - sz.Height) / 2 : r.Bottom - sz.Height;
        Draw(s, f, c, x, y);
    }

    // composite all queued strings onto a premultiplied 32-bit bitmap
    public void RenderTo(Bitmap target)
    {
        if (ops.Count == 0) return;
        var tr = target.LockBits(new Rectangle(0, 0, target.Width, target.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var t = new byte[tr.Stride * target.Height];
            Marshal.Copy(tr.Scan0, t, 0, t.Length);
            foreach (var op in ops) Composite(op, t, tr.Stride, target.Width, target.Height);
            Marshal.Copy(t, 0, tr.Scan0, t.Length);
        }
        finally { target.UnlockBits(tr); }
        ops.Clear();
    }

    static void Composite(Op op, byte[] t, int stride, int tw, int th)
    {
        var sz = Measure(op.Text, op.Font);
        int mw = sz.Width + 4, mh = sz.Height + 2; // room for overhanging glyphs (italics, f, j)
        using (var mask = new Bitmap(mw, mh, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(mask))
            {
                g.Clear(Color.Black);
                IntPtr hf = CreateFont(op.Font), dc = g.GetHdc(), old = SelectObject(dc, hf);
                SetTextColor(dc, 0xFFFFFF);
                SetBkMode(dc, 1 /* TRANSPARENT */);
                TextOut(dc, 1, 0, op.Text, op.Text.Length);
                SelectObject(dc, old);
                DeleteObject(hf);
                g.ReleaseHdc(dc);
            }
            var mr = mask.LockBits(new Rectangle(0, 0, mw, mh), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var m = new byte[mr.Stride * mh];
                Marshal.Copy(mr.Scan0, m, 0, m.Length);
                int ca = op.Color.A, cr = op.Color.R, cg = op.Color.G, cb = op.Color.B;
                for (int yy = 0; yy < mh; yy++)
                {
                    int ty = op.Y + yy;
                    if (ty < 0 || ty >= th) continue;
                    for (int xx = 0; xx < mw; xx++)
                    {
                        int tx = op.X - 1 + xx;
                        if (tx < 0 || tx >= tw) continue;
                        int mi = yy * mr.Stride + xx * 3;
                        int cov = (m[mi] + m[mi + 1] + m[mi + 2]) / 3;
                        if (cov == 0) continue;
                        int a = cov * ca / 255, inv = 255 - a;
                        int ti = ty * stride + tx * 4; // BGRA, premultiplied
                        t[ti] = (byte)((cb * a + t[ti] * inv) / 255);
                        t[ti + 1] = (byte)((cg * a + t[ti + 1] * inv) / 255);
                        t[ti + 2] = (byte)((cr * a + t[ti + 2] * inv) / 255);
                        t[ti + 3] = (byte)(a + t[ti + 3] * inv / 255);
                    }
                }
            }
            finally { mask.UnlockBits(mr); }
        }
    }

    // ClearType hinting regardless of the system's font smoothing setting.
    // The LOGFONT must be taken before any GetHdc: GDI+ locks a Graphics while its HDC is out.
    static readonly Dictionary<Font, LOGFONT> logFonts = new Dictionary<Font, LOGFONT>();

    static IntPtr CreateFont(Font f)
    {
        LOGFONT lf;
        if (!logFonts.TryGetValue(f, out lf))
        {
            lf = new LOGFONT();
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp)) f.ToLogFont(lf, g); // bitmap graphics = 96 dpi, like the popup bitmap
            lf.lfQuality = CLEARTYPE_QUALITY;
            logFonts[f] = lf;
        }
        return CreateFontIndirect(lf);
    }
}
