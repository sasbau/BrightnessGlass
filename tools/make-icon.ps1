# Generates app.ico (monitor with day/night screen: sun + moon) in 16..256 px, PNG-compressed entries.
param([string]$Out = (Join-Path $PSScriptRoot '..\assets\app.ico'))
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

public static class IconArt
{
    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    // drawn on a 256x256 canvas, scaled to size
    public static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size / 256f, size / 256f);
            bool small = size <= 24;

            // stand
            using (var b = new LinearGradientBrush(new PointF(0, 186), new PointF(0, 230), Color.FromArgb(150, 156, 168), Color.FromArgb(96, 102, 114)))
            {
                g.FillRectangle(b, 112, 184, 32, 30);
                using (var p = Round(new RectangleF(70, 210, 116, 18), 9)) g.FillPath(b, p);
            }

            // bezel
            var body = new RectangleF(14, 30, 228, 160);
            using (var p = Round(body, 18))
            using (var b = new LinearGradientBrush(body, Color.FromArgb(58, 63, 76), Color.FromArgb(24, 26, 33), 90f))
                g.FillPath(b, p);

            // screen, split diagonally into day (top-left) and night (bottom-right)
            var scr = small ? new RectangleF(26, 42, 204, 136) : new RectangleF(28, 44, 200, 132);
            using (var clip = Round(scr, 8))
            {
                var old = g.Clip; g.SetClip(clip);
                using (var day = new LinearGradientBrush(scr, Color.FromArgb(255, 196, 92), Color.FromArgb(120, 190, 255), 60f))
                    g.FillRectangle(day, scr);
                var night = new PointF[] { new PointF(scr.Right + 40, scr.Top - 30), new PointF(scr.Right + 40, scr.Bottom + 10), new PointF(scr.Left + 40, scr.Bottom + 10) };
                using (var nb = new LinearGradientBrush(scr, Color.FromArgb(46, 62, 130), Color.FromArgb(14, 20, 52), 60f))
                    g.FillPolygon(nb, night);

                // sun
                float sx = 84, sy = 92, sr = small ? 26 : 21;
                using (var sun = new SolidBrush(Color.FromArgb(255, 250, 226, 92)))
                {
                    g.FillEllipse(sun, sx - sr, sy - sr, 2 * sr, 2 * sr);
                    if (!small)
                        using (var pen = new Pen(sun.Color, 7) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            for (int i = 0; i < 8; i++)
                            {
                                double a = i * Math.PI / 4;
                                g.DrawLine(pen, sx + (float)Math.Cos(a) * 30, sy + (float)Math.Sin(a) * 30, sx + (float)Math.Cos(a) * 38, sy + (float)Math.Sin(a) * 38);
                            }
                }

                // crescent moon
                float mx = 176, my = 128, mr = small ? 26 : 22;
                using (var moon = new GraphicsPath())
                using (var cut = new GraphicsPath())
                {
                    moon.AddEllipse(mx - mr, my - mr, 2 * mr, 2 * mr);
                    cut.AddEllipse(mx - mr + mr * 0.65f, my - mr - mr * 0.35f, 2 * mr, 2 * mr);
                    var reg = new Region(moon); reg.Exclude(cut);
                    using (var mb = new SolidBrush(Color.FromArgb(246, 241, 214))) g.FillRegion(mb, reg);
                }

                // stars
                if (!small)
                    using (var st = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                    {
                        g.FillEllipse(st, 200, 92, 5, 5); g.FillEllipse(st, 148, 158, 4, 4); g.FillEllipse(st, 208, 154, 4, 4);
                    }
                g.Clip = old;
            }
        }
        return bmp;
    }
}
'@ -ReferencedAssemblies System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = [IconArt]::Draw($s); $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); , $ms.ToArray()
}
$fs = [IO.File]::Create($Out); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
# preview for review
$prev = [IconArt]::Draw(256); $prev.Save([IO.Path]::ChangeExtension($Out, '.png')); $prev.Dispose()
"written $Out"
