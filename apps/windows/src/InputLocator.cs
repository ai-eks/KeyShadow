using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Accessibility;

namespace keyshadow
{
    internal sealed class InputAnchor
    {
        internal readonly IntPtr Foreground;
        internal readonly Rectangle Bounds;
        internal readonly string Source;
        internal readonly bool IsCaret;

        internal InputAnchor(IntPtr foreground, Rectangle bounds, string source, bool isCaret)
        {
            Foreground = foreground; Bounds = bounds; Source = source; IsCaret = isCaret;
        }
    }

    // Run the accessibility fallback on background workers. Calls share no provider objects.
    // Only state and geometry are read, never text or values.
    internal sealed class InputLocator
    {
        private readonly uint ownProcess = GetCurrentProcessId();

        internal bool TryGet(out InputAnchor anchor)
        {
            anchor = null;
            try { return TryGetNative(out anchor) || TryGetCore(out anchor); }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        // Uses only local Win32 metadata, so a blocked accessibility provider cannot stall this path.
        internal bool TryGetNative(out InputAnchor anchor)
        {
            anchor = null;
            IntPtr foreground;
            uint thread;
            GuiInfo gui;
            bool nativeEdit;
            if (!ReadContext(out foreground, out thread, out gui, out nativeEdit) || !nativeEdit
                || gui.Caret != gui.Focus) return false;
            Rectangle bounds;
            if (!TryNativeCaret(gui, foreground, out bounds) || !SameContext(foreground, thread, gui)
                || (GetWindowLong(gui.Focus, -16) & (0x20 | 0x800)) != 0) return false;
            anchor = new InputAnchor(foreground, bounds, "Win32", true);
            return true;
        }

        private bool TryGetCore(out InputAnchor anchor)
        {
            anchor = null;
            IntPtr foreground;
            uint thread;
            GuiInfo gui;
            bool nativeEdit;
            if (!ReadContext(out foreground, out thread, out gui, out nativeEdit)) return false;

            Rectangle field;
            TextPattern text;
            AutomationElement focusedElement;
            bool editable;
            int focusState = InspectFocus(foreground, out field, out text, out editable, out focusedElement);
            if (focusState < 0 || (focusState == 0 && !nativeEdit)) return false;

            Rectangle bounds;
            string source = null;
            bool caret = true;
            if (TryNativeCaret(gui, foreground, out bounds)) source = "Win32";
            else if (TryAccessibleCaret(gui.Focus, out bounds)
                || (gui.Focus != foreground && TryAccessibleCaret(foreground, out bounds))) source = "MSAA";
            else if (editable && TryTextBounds(text, out bounds)) source = "UIA";
            else if (editable && field.Width > 0 && field.Height > 0 && field.Height <= 160)
            {
                bounds = field; source = "InputField"; caret = false;
            }
            if (source == null || !SameContext(foreground, thread, gui)) return false;
            // Browser fields share an HWND, so also recheck the original virtual element.
            if (focusedElement != null && (!focusedElement.Current.HasKeyboardFocus
                || focusedElement.Current.IsPassword || focusedElement.Current.IsOffscreen)) return false;
            anchor = new InputAnchor(foreground, bounds, source, caret);
            return true;
        }

        private bool ReadContext(out IntPtr foreground, out uint thread, out GuiInfo gui, out bool nativeEdit)
        {
            foreground = GetForegroundWindow();
            gui = new GuiInfo();
            nativeEdit = false;
            uint process;
            thread = GetWindowThreadProcessId(foreground, out process);
            if (thread == 0 || process == ownProcess || !IsWindowVisible(foreground) || IsIconic(foreground)) return false;
            gui.Size = Marshal.SizeOf(typeof(GuiInfo));
            if (!GetGUIThreadInfo(thread, ref gui) || gui.Focus == IntPtr.Zero
                || !IsWindowVisible(gui.Focus) || !IsWindowEnabled(gui.Focus) || (gui.Flags & 0x1e) != 0) return false;

            var className = new StringBuilder(256);
            GetClassName(gui.Focus, className, className.Capacity);
            string name = className.ToString();
            nativeEdit = name.Equals("Edit", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf(".EDIT.", StringComparison.OrdinalIgnoreCase) >= 0;
            // These style bits apply to edit controls, not arbitrary custom windows.
            if (nativeEdit && (GetWindowLong(gui.Focus, -16) & (0x20 | 0x800)) != 0) return false;
            return true;
        }

        private static bool SameContext(IntPtr foreground, uint thread, GuiInfo gui)
        {
            // A slow accessibility provider can complete after the user changes focus.
            var latest = new GuiInfo();
            latest.Size = Marshal.SizeOf(typeof(GuiInfo));
            return GetForegroundWindow() == foreground && GetGUIThreadInfo(thread, ref latest)
                && latest.Focus == gui.Focus && (latest.Flags & 0x1e) == 0;
        }

        // -1 explicitly unsuitable; 0 unavailable; 1 inspected. A provider failure is not permission
        // to guess that a custom control is writable. Recognized native edits retain the Win32 path.
        private int InspectFocus(IntPtr foreground, out Rectangle field, out TextPattern text,
            out bool editable, out AutomationElement focusedElement)
        {
            field = Rectangle.Empty; text = null; editable = false; focusedElement = null;
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null) return 0;
                focusedElement = focused;
                var info = focused.Current;
                if (info.ProcessId == ownProcess || !info.HasKeyboardFocus || !info.IsEnabled
                    || info.IsOffscreen || info.IsPassword) return -1;
                IntPtr window = new IntPtr(info.NativeWindowHandle);
                if (window != IntPtr.Zero && window != foreground && !IsChild(foreground, window)) return -1;

                object pattern;
                if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                {
                    if (((ValuePattern)pattern).Current.IsReadOnly) return -1;
                    editable = true;
                }
                if (focused.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                {
                    text = (TextPattern)pattern;
                    object readOnly = text.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                    if (readOnly is bool)
                    {
                        if ((bool)readOnly) return -1;
                        editable = true;
                    }
                }
                // An edit role alone does not prove writability: require an explicit pattern state.
                if (editable) TryRectangle(info.BoundingRectangle, out field);
                return 1;
            }
            catch (ElementNotAvailableException) { return 0; }
            catch (InvalidOperationException) { return 0; }
            catch (COMException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private static bool TryNativeCaret(GuiInfo gui, IntPtr foreground, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (gui.Caret == IntPtr.Zero || !IsWindowVisible(gui.Caret)
                || (gui.Caret != foreground && !IsChild(foreground, gui.Caret))
                || gui.CaretRect.Bottom <= gui.CaretRect.Top) return false;
            var first = new PointNative(gui.CaretRect.Left, gui.CaretRect.Top);
            var last = new PointNative(gui.CaretRect.Right, gui.CaretRect.Bottom);
            IntPtr previous = IntPtr.Zero;
            try
            {
                // rcCaret is in the target window's logical client coordinates. Map it in that
                // window's DPI context, then explicitly convert to physical screen coordinates.
                previous = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(gui.Caret));
                if (previous == IntPtr.Zero || !ClientToScreen(gui.Caret, ref first)
                    || !ClientToScreen(gui.Caret, ref last)
                    || !LogicalToPhysicalPointForPerMonitorDPI(gui.Caret, ref first)
                    || !LogicalToPhysicalPointForPerMonitorDPI(gui.Caret, ref last)) return false;
                if (last.Y <= first.Y || last.X < first.X) return false;
                bounds = new Rectangle(first.X, first.Y, Math.Max(1, last.X - first.X), last.Y - first.Y);
                return true;
            }
            catch (EntryPointNotFoundException) { return false; }
            finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        }

        private static bool TryAccessibleCaret(IntPtr window, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            IAccessible accessible = null;
            try
            {
                Guid id = typeof(IAccessible).GUID;
                if (AccessibleObjectFromWindow(window, unchecked((uint)-8), ref id, out accessible) != 0
                    || accessible == null) return false;
                int state = Convert.ToInt32(accessible.get_accState(0));
                // Unavailable, invisible, offscreen or protected accessibility object.
                if ((state & (0x1 | 0x8000 | 0x10000 | 0x20000000)) != 0) return false;
                int left, top, width, height;
                accessible.accLocation(out left, out top, out width, out height, 0);
                if (height <= 0 || width < 0) return false;
                bounds = new Rectangle(left, top, Math.Max(1, width), height);
                return true;
            }
            catch (COMException) { return false; }
            catch (ArgumentException) { return false; }
            catch (InvalidCastException) { return false; }
            finally { if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible); }
        }

        private static bool TryTextBounds(TextPattern text, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (text == null) return false;
            try
            {
                var ranges = text.GetSelection();
                if (ranges.Length != 1) return false;
                var range = ranges[0];
                var rectangles = range.GetBoundingRectangles();
                bool collapsed = range.CompareEndpoints(TextPatternRangeEndpoint.Start,
                    range, TextPatternRangeEndpoint.End) == 0;
                if (rectangles.Length > 0)
                {
                    if (!TryRectangle(rectangles[rectangles.Length - 1], out bounds)) return false;
                    // A selected range has two endpoints; anchor at its final visible edge.
                    bounds = new Rectangle(collapsed ? bounds.Left : bounds.Right, bounds.Top, 1, bounds.Height);
                    return true;
                }
                if (!collapsed) return false;

                // Degenerate ranges often have no rectangle. Expand a clone by one character
                // to obtain only its geometry; the actual selection and text are untouched.
                var next = range.Clone();
                if (next.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) > 0)
                {
                    rectangles = next.GetBoundingRectangles();
                    if (rectangles.Length > 0 && TryRectangle(rectangles[0], out bounds))
                    {
                        bounds = new Rectangle(bounds.Left, bounds.Top, 1, bounds.Height);
                        return true;
                    }
                }
                var previous = range.Clone();
                if (previous.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) < 0)
                {
                    rectangles = previous.GetBoundingRectangles();
                    if (rectangles.Length > 0 && TryRectangle(rectangles[rectangles.Length - 1], out bounds))
                    {
                        bounds = new Rectangle(bounds.Right, bounds.Top, 1, bounds.Height);
                        return true;
                    }
                }
                return false;
            }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (COMException) { return false; }
        }

        private static bool TryRectangle(System.Windows.Rect rect, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (rect.IsEmpty || double.IsNaN(rect.X) || double.IsNaN(rect.Y)
                || double.IsNaN(rect.Width) || double.IsNaN(rect.Height)
                || double.IsInfinity(rect.X) || double.IsInfinity(rect.Y)
                || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height)
                || rect.Height <= 0 || rect.Width < 0 || rect.Width > int.MaxValue || rect.Height > int.MaxValue
                || rect.X < int.MinValue || rect.Y < int.MinValue
                || rect.Right > int.MaxValue || rect.Bottom > int.MaxValue) return false;
            bounds = new Rectangle((int)Math.Floor(rect.X), (int)Math.Floor(rect.Y),
                Math.Max(1, (int)Math.Ceiling(rect.Width)), Math.Max(1, (int)Math.Ceiling(rect.Height)));
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PointNative
        {
            internal int X, Y;
            internal PointNative(int x, int y) { X = x; Y = y; }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct RectNative { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiInfo
        {
            internal int Size, Flags;
            internal IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            internal RectNative CaretRect;
        }

        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref PointNative point);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr window, ref PointNative point);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId,
            ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
    }
}
