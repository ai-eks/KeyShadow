using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using keyshadow;

internal static class GenerateIcon
{
    private static void Main(string[] args)
    {
        string output = args.Length == 0 ? "assets" : args[0];
        string branding = args.Length > 1 ? args[1] : output;
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(branding);
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        byte[][] images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using (Bitmap image = AppIcon.Render(sizes[i]))
                images[i] = EncodeIconBitmap(image);
        }

        using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "app.ico"))))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
        using (Bitmap logo = AppIcon.Render(512))
            logo.Save(Path.Combine(branding, "logo.png"), ImageFormat.Png);
        using (Bitmap logo = AppIcon.Render(128))
            logo.Save(Path.Combine(branding, "logo-preview.png"), ImageFormat.Png);
        // Mirrors AppIcon.Render at full size; the glass blurs a copy of the scene through a clip.
        File.WriteAllText(Path.Combine(branding, "logo.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"512\" height=\"512\" viewBox=\"0 0 512 512\" role=\"img\" aria-labelledby=\"title\">\n" +
            "  <title id=\"title\">键影：玻璃键帽「键」与它的「影」</title>\n" +
            "  <defs>\n" +
            "    <linearGradient id=\"tile\" x1=\"0\" y1=\"24\" x2=\"0\" y2=\"488\" gradientUnits=\"userSpaceOnUse\"><stop offset=\"0\" stop-color=\"#1c2536\"/><stop offset=\"1\" stop-color=\"#0e131c\"/></linearGradient>\n" +
            "    <linearGradient id=\"key\" x1=\"168\" y1=\"168\" x2=\"468\" y2=\"468\" gradientUnits=\"userSpaceOnUse\"><stop offset=\"0\" stop-color=\"#7cf0d6\"/><stop offset=\"1\" stop-color=\"#2b8cff\"/></linearGradient>\n" +
            "    <clipPath id=\"glass\"><rect x=\"44\" y=\"44\" width=\"300\" height=\"300\" rx=\"80\"/></clipPath>\n" +
            "    <filter id=\"frost\" x=\"0\" y=\"0\" width=\"512\" height=\"512\" filterUnits=\"userSpaceOnUse\"><feGaussianBlur stdDeviation=\"18\"/></filter>\n" +
            "    <g id=\"scene\">\n" +
            "      <rect x=\"24\" y=\"24\" width=\"464\" height=\"464\" rx=\"108\" fill=\"url(#tile)\"/>\n" +
            "      <rect x=\"168\" y=\"168\" width=\"300\" height=\"300\" rx=\"80\" fill=\"url(#key)\"/>\n" +
            "      <path fill=\"#0b2447\" fill-rule=\"evenodd\" d=\"" + SvgGlyph("影", new RectangleF(234, 234, 168, 168)) + "\"/>\n" +
            "    </g>\n" +
            "  </defs>\n" +
            "  <use xlink:href=\"#scene\"/>\n" +
            "  <g clip-path=\"url(#glass)\"><g filter=\"url(#frost)\"><rect width=\"512\" height=\"512\" fill=\"url(#tile)\"/><use xlink:href=\"#scene\"/></g></g>\n" +
            "  <rect x=\"44\" y=\"44\" width=\"300\" height=\"300\" rx=\"80\" fill=\"#fff\" fill-opacity=\".165\" stroke=\"#fff\" stroke-opacity=\".55\" stroke-width=\"4\"/>\n" +
            "  <path fill=\"#fff\" fill-rule=\"evenodd\" d=\"" + SvgGlyph("键", new RectangleF(110, 110, 168, 168)) + "\"/>\n" +
            "</svg>\n");
        using (var preview = new Bitmap(560, 164))
        using (Graphics g = Graphics.FromImage(preview))
        using (var font = new Font("Arial", 9))
        using (var dark = new SolidBrush(Color.FromArgb(23, 31, 43)))
        {
            g.Clear(Color.White);
            g.FillRectangle(dark, 0, 82, 560, 82);
            int[] previewSizes = { 16, 20, 24, 27, 32, 48, 64 };
            for (int i = 0; i < previewSizes.Length; i++)
            {
                int size = previewSizes[i], x = i * 80 + (80 - size) / 2;
                g.DrawString(size + " px", font, Brushes.Gray, i * 80 + 14, 0);
                using (Bitmap logo = AppIcon.Render(size))
                {
                    g.DrawImageUnscaled(logo, x, 18);
                    g.DrawImageUnscaled(logo, x, 98);
                }
            }
            preview.Save(Path.Combine(branding, "logo-sizes.png"), ImageFormat.Png);
        }
        Console.WriteLine("Created " + Path.GetFullPath(output));
    }

    private static string SvgGlyph(string text, RectangleF bounds)
    {
        using (GraphicsPath glyph = AppIcon.Glyph(text, bounds))
        {
            PointF[] points = glyph.PathPoints;
            byte[] types = glyph.PathTypes;
            var data = new StringBuilder();
            for (int i = 0; i < points.Length; i++)
            {
                int type = types[i] & 7;
                data.Append(type == 0 ? 'M' : type == 1 ? 'L' : 'C');
                int count = type == 3 ? 3 : 1;
                for (int point = 0; point < count; point++, i++)
                {
                    data.Append(points[i].X.ToString("0.#", CultureInfo.InvariantCulture)).Append(' ');
                    data.Append(points[i].Y.ToString("0.#", CultureInfo.InvariantCulture)).Append(' ');
                }
                i--;
                if ((types[i] & 128) != 0) data.Append('Z');
            }
            return data.ToString();
        }
    }

    // Classic 32-bit ICO bitmaps work with the Framework compiler and Explorer.
    private static byte[] EncodeIconBitmap(Bitmap image)
    {
        int size = image.Width;
        int maskStride = ((size + 31) / 32) * 4;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(size * size * 4 + maskStride * size);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            for (int y = size - 1; y >= 0; y--)
                for (int x = 0; x < size; x++)
                {
                    Color pixel = image.GetPixel(x, y);
                    writer.Write(pixel.B);
                    writer.Write(pixel.G);
                    writer.Write(pixel.R);
                    writer.Write(pixel.A);
                }
            for (int y = size - 1; y >= 0; y--)
            {
                byte[] mask = new byte[maskStride];
                for (int x = 0; x < size; x++)
                    if (image.GetPixel(x, y).A == 0) mask[x / 8] |= (byte)(0x80 >> (x % 8));
                writer.Write(mask);
            }
            return stream.ToArray();
        }
    }
}
