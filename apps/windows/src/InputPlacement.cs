using System;
using System.Drawing;

namespace keyshadow
{
    internal static class InputPlacement
    {
        // Prefer above the insertion point; reserve space for a typical IME candidate row.
        internal static Point Place(Rectangle anchor, Size keyboard, Rectangle workArea, int gap)
        {
            int above = anchor.Top - workArea.Top - gap;
            int below = workArea.Bottom - anchor.Bottom - gap;
            int y = above >= keyboard.Height || above >= below
                ? anchor.Top - gap - keyboard.Height : anchor.Bottom + gap;
            int x = anchor.Left;
            x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - keyboard.Width));
            y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - keyboard.Height));
            return new Point(x, y);
        }
    }
}
