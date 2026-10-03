using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using keyshadow;

internal static class RenderingTests
{
    private static int checks;
    private static readonly Color background = Color.FromArgb(180, 40, 160);

    [STAThread]
    private static int Main(string[] args)
    {
        byte[] saved = File.Exists(Preferences.PathName) ? File.ReadAllBytes(Preferences.PathName) : null;
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            using (var backdrop = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                Bounds = area, BackColor = background, TopMost = true })
            using (var keyboard = new KeyboardForm(new Preferences { FollowInput = false, ThemeId = "cream",
                Size = .5f, X = area.Left + 40, Y = area.Top + 40 }, InputMethod.IsChinese, delegate { return false; }))
            {
                backdrop.Show(); backdrop.Activate();
                Pump();
                IntPtr foreground = GetForegroundWindow();
                // Keep an explicit preview while testing pixels; IME gating is covered separately.
                typeof(KeyboardForm).GetField("previewUntil", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(keyboard, DateTime.MaxValue);
                keyboard.Show(); Pump();
                foreach (string theme in new[] { "navy", "cream" })
                foreach (bool compact in new[] { false, true })
                foreach (int size in new[] { 50, 85, 150 })
                foreach (int opacity in new[] { 35, 95 })
                {
                    keyboard.ChangeTheme(KeyboardTheme.Find(theme));
                    keyboard.SetCompact(compact); keyboard.ChangeSize(size); keyboard.ChangeOpacity(opacity);
                    Pump();
                    string label = theme + " compact=" + compact + " size=" + size + " opacity=" + opacity;
                    using (Bitmap frame = keyboard.RenderFrame())
                    using (Bitmap screenshot = Capture(keyboard))
                    {
                        Check(frame.GetPixel(0, 0).A == 0 && frame.GetPixel(frame.Width / 2, frame.Height / 2).A == 255,
                            "transparent outside and opaque content: " + label);
                        int corner = (int)Math.Ceiling(26 * frame.Width / (compact ? 796f : 820f));
                        var samples = new List<Point>();
                        var levels = new HashSet<byte>();
                        foreach (Point origin in new[] { Point.Empty, new Point(frame.Width - corner, 0),
                            new Point(0, frame.Height - corner), new Point(frame.Width - corner, frame.Height - corner) })
                        {
                            int partial = 0;
                            for (int y = origin.Y; y < origin.Y + corner; y++)
                            for (int x = origin.X; x < origin.X + corner; x++)
                            {
                                byte alpha = frame.GetPixel(x, y).A;
                                if (alpha > 0 && alpha < 255)
                                {
                                    partial++; levels.Add(alpha);
                                    samples.Add(new Point(x, y));
                                }
                            }
                            Check(partial > 4, "each rounded corner has fractional coverage: " + label + " " + origin);
                        }
                        Check(levels.Count >= 4, "edge has multiple alpha levels: " + label);
                        samples.Add(Point.Empty);
                        samples.Add(new Point(frame.Width / 2, frame.Height / 2));
                        foreach (Point point in samples)
                        {
                            Color source = frame.GetPixel(point.X, point.Y), actual = screenshot.GetPixel(point.X, point.Y);
                            double alpha = source.A / 255.0 * Math.Round(opacity / 100.0 * 255) / 255.0;
                            int r = (int)Math.Round(source.R * alpha + background.R * (1 - alpha));
                            int g = (int)Math.Round(source.G * alpha + background.G * (1 - alpha));
                            int b = (int)Math.Round(source.B * alpha + background.B * (1 - alpha));
                            if (Math.Abs(actual.R - r) > 4 || Math.Abs(actual.G - g) > 4 || Math.Abs(actual.B - b) > 4)
                                throw new Exception("Desktop composition mismatch " + label + " at " + point
                                    + " actual=" + actual + " expected=" + Color.FromArgb(r, g, b));
                        }
                        Check(true, "real desktop combines edge alpha and overall opacity: " + label);
                    }
                }
                keyboard.SetCompact(false); keyboard.ChangeSize(85); keyboard.ChangeOpacity(95); Pump();
                Point center = keyboard.PointToScreen(new Point(keyboard.Width / 2, keyboard.Height / 2));
                Check(WindowFromPoint(keyboard.Location) == backdrop.Handle, "transparent corner lets pointer reach the backdrop");
                Check(WindowFromPoint(center) == keyboard.Handle, "visible keyboard accepts pointer input");
                keyboard.SetPassThrough(true); Pump();
                Check(WindowFromPoint(center) == backdrop.Handle, "click-through still reaches the backdrop");
                keyboard.SetPassThrough(false);
                Point before = keyboard.Location;
                keyboard.Location = new Point(before.X + 40, before.Y + 30); Pump();
                Check(WindowFromPoint(keyboard.PointToScreen(new Point(40, 40))) == keyboard.Handle, "moved layered window keeps its input surface");
                using (Bitmap screenshot = Capture(keyboard)) screenshot.Save(Path.Combine(args[0], "rendering-desktop.png"), ImageFormat.Png);
                using (Bitmap frame = keyboard.RenderFrame()) frame.Save(Path.Combine(args[0], "rendering-alpha.png"), ImageFormat.Png);
                uint handles = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                for (int i = 0; i < 40; i++) { keyboard.ChangeOpacity(i % 2 == 0 ? 35 : 95); Pump(); }
                Check(GetGuiResources(Process.GetCurrentProcess().Handle, 0) <= handles + 2, "repeated redraw releases GDI handles");
                keyboard.Hide(); keyboard.Show(); Pump();
                Check(WindowFromPoint(keyboard.PointToScreen(new Point(40, 40))) == keyboard.Handle, "show after hide restores the alpha surface");
                Check(GetForegroundWindow() == foreground, "redraw and show preserve the original foreground window");
                keyboard.Close(); backdrop.Close();
            }
            Console.WriteLine("SUCCESS " + checks + " rendering checks"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            if (saved != null) File.WriteAllBytes(Preferences.PathName, saved);
            else if (File.Exists(Preferences.PathName)) File.Delete(Preferences.PathName);
        }
    }

    private static Bitmap Capture(KeyboardForm keyboard)
    {
        DwmFlush();
        var bitmap = new Bitmap(keyboard.Width, keyboard.Height);
        using (Graphics g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(keyboard.Location, Point.Empty, keyboard.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }
    private static void Pump() { Application.DoEvents(); Thread.Sleep(35); Application.DoEvents(); DwmFlush(); }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++; Console.WriteLine("PASS " + label);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
}
