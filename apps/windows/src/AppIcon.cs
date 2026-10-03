using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace keyshadow
{
    internal static class AppIcon
    {
        internal static Icon Create()
        {
            using (Bitmap bitmap = Render(GetSystemMetrics(49))) // SM_CXSMICON: native tray icon size.
            {
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon icon = Icon.FromHandle(handle)) return (Icon)icon.Clone();
                }
                finally { DestroyIcon(handle); }
            }
        }

        internal static Bitmap Render(int size)
        {
            // Every size draws the full design, tray icons included.
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Bitmap frosted = Frosted(size))
            using (Graphics g = Begin(bitmap))
            {
                DrawScene(g);

                // A frosted glass 键 key floats in front of its 影, blurring what lies behind it.
                // The glass edge stays at least half a pixel wide so small icons keep its outline.
                using (GraphicsPath glass = Round(44, 44, 300, 300, 80))
                using (var tint = new SolidBrush(Color.FromArgb(42, 255, 255, 255)))
                using (var edge = new Pen(Color.FromArgb(140, 255, 255, 255), Math.Max(4f, 256f / size)))
                {
                    g.SetClip(glass);
                    GraphicsState state = g.Save();
                    g.ResetTransform();
                    g.DrawImageUnscaled(frosted, 0, 0);
                    g.Restore(state);
                    g.FillPath(tint, glass);
                    g.ResetClip();
                    g.DrawPath(edge, glass);
                }
                Fill(g, Glyph("键", new RectangleF(110, 110, 168, 168)), new SolidBrush(Color.White));
            }
            return bitmap;
        }

        internal static GraphicsPath Glyph(string text, RectangleF bounds)
        {
            var path = new GraphicsPath();
            using (var family = new FontFamily("Microsoft YaHei UI"))
                path.AddString(text, family, (int)FontStyle.Bold, 256, PointF.Empty, StringFormat.GenericTypographic);
            RectangleF ink = path.GetBounds();
            float scale = Math.Min(bounds.Width / ink.Width, bounds.Height / ink.Height);
            using (var transform = new Matrix(scale, 0, 0, scale,
                bounds.X + (bounds.Width - ink.Width * scale) / 2 - ink.X * scale,
                bounds.Y + (bounds.Height - ink.Height * scale) / 2 - ink.Y * scale))
                path.Transform(transform);
            return path;
        }

        private static Graphics Begin(Bitmap bitmap)
        {
            Graphics g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(bitmap.Width / 512f, bitmap.Height / 512f);
            return g;
        }

        // Everything behind the glass: the tile and the gradient key carrying 影.
        private static void DrawScene(Graphics g)
        {
            Fill(g, Round(24, 24, 464, 464, 108), TileBrush());
            Fill(g, Round(168, 168, 300, 300, 80), new LinearGradientBrush(
                new RectangleF(168, 168, 300, 300), Rgb(0x7CF0D6), Rgb(0x2B8CFF), LinearGradientMode.ForwardDiagonal));
            Fill(g, Glyph("影", new RectangleF(234, 234, 168, 168)), new SolidBrush(Rgb(0x0B2447)));
        }

        // The scene on an opaque tile-colored canvas, blurred by three box passes (close to a Gaussian, σ = 18 units).
        private static Bitmap Frosted(int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Begin(bitmap))
            using (Brush tile = TileBrush())
            {
                g.FillRectangle(tile, 0, 0, 512, 512);
                DrawScene(g);
            }
            // Three box passes of radius r give σ² = r(r + 1); below a pixel, antialiasing already blurs enough.
            double sigma = 18.0 * size / 512;
            int radius = (int)Math.Round((Math.Sqrt(1 + 4 * sigma * sigma) - 1) / 2);
            if (radius == 0) return bitmap;
            var bounds = new Rectangle(0, 0, size, size);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var pixels = new int[size * size];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            Blur(pixels, size, radius);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bitmap.UnlockBits(data);
            return bitmap;
        }

        private static void Blur(int[] pixels, int size, int radius)
        {
            var line = new int[size];
            int width = radius * 2 + 1;
            for (int pass = 0; pass < 6; pass++)
            {
                bool horizontal = pass % 2 == 0;
                for (int a = 0; a < size; a++)
                {
                    for (int i = 0; i < size; i++) line[i] = pixels[horizontal ? a * size + i : i * size + a];
                    int r = 0, gr = 0, b = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int c = line[Math.Min(size - 1, Math.Max(0, k))];
                        r += (c >> 16) & 255; gr += (c >> 8) & 255; b += c & 255;
                    }
                    for (int i = 0; i < size; i++)
                    {
                        pixels[horizontal ? a * size + i : i * size + a] =
                            unchecked((int)0xFF000000) | ((r / width) << 16) | ((gr / width) << 8) | (b / width);
                        int add = line[Math.Min(size - 1, i + radius + 1)], remove = line[Math.Max(0, i - radius)];
                        r += ((add >> 16) & 255) - ((remove >> 16) & 255);
                        gr += ((add >> 8) & 255) - ((remove >> 8) & 255);
                        b += (add & 255) - (remove & 255);
                    }
                }
            }
        }

        private static Brush TileBrush()
        {
            return new LinearGradientBrush(new RectangleF(0, 24, 512, 464), Rgb(0x1C2536), Rgb(0x0E131C), LinearGradientMode.Vertical);
        }

        private static Color Rgb(int rgb)
        {
            return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }

        private static void Fill(Graphics g, GraphicsPath path, Brush brush)
        {
            using (path)
            using (brush)
                g.FillPath(brush, path);
        }

        private static GraphicsPath Round(float x, float y, float width, float height, float radius)
        {
            float diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
    }
}
