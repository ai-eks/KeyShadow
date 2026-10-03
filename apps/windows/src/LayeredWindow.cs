using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace keyshadow
{
    internal static class LayeredWindow
    {
        internal const int Style = 0x80000;

        // The bitmap uses premultiplied alpha; its edge coverage is combined with overall opacity.
        internal static void Present(IntPtr window, Point location, Bitmap bitmap, byte opacity)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr image = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                image = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(dc, image);
                var origin = new Point();
                var size = bitmap.Size;
                var blend = new Blend { ConstantAlpha = opacity, AlphaFormat = 1 };
                if (!UpdateLayeredWindow(window, IntPtr.Zero, ref location, ref size, dc, ref origin, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(dc, previous);
                if (image != IntPtr.Zero) DeleteObject(image);
                if (dc != IntPtr.Zero) DeleteDC(dc);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Blend { internal byte Operation, Flags, ConstantAlpha, AlphaFormat; }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr image);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr image);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(
            IntPtr window, IntPtr screen, ref Point destination, ref Size size, IntPtr source,
            ref Point origin, uint colorKey, ref Blend blend, uint flags);
    }
}
