using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace keyshadow
{
    // Only focus metadata, the password flag and read-only state are read, never field names, text or values.
    // Results: true = an identified password or read-only field (no hints); false = an ordinary field;
    // null = unknown. Callers treat unknown like an ordinary field, as on macOS.
    internal static class InputPrivacy
    {
        internal static bool? ReadNative(out IntPtr foreground, out IntPtr focus)
        {
            foreground = KeyboardMonitor.GetForegroundWindow();
            focus = FocusWindow(foreground);
            if (focus == IntPtr.Zero) return null;
            var name = new StringBuilder(256);
            GetClassName(focus, name, name.Capacity);
            string value = name.ToString();
            if (value.Equals("Edit", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
                || value.IndexOf(".EDIT.", StringComparison.OrdinalIgnoreCase) >= 0)
                return (Native.GetWindowLong(focus, -16) & (0x20 | 0x800)) != 0; // ES_PASSWORD, ES_READONLY
            return null; // Browser and WPF fields require inspecting their virtual focused element.
        }

        // Run off the UI and keyboard-hook threads: an accessibility provider can be slow.
        // stale: focus moved while reading, so the result describes another field and must be re-read.
        internal static bool? ReadAccessible(IntPtr foreground, IntPtr focus, out bool stale)
        {
            stale = false;
            try
            {
                if (!SameFocus(foreground, focus)) { stale = true; return null; }
                AutomationElement element = AutomationElement.FocusedElement;
                if (element == null) return null;
                var info = element.Current;
                if (!info.HasKeyboardFocus) { stale = true; return null; }
                if (info.IsOffscreen) return null;
                IntPtr window = new IntPtr(info.NativeWindowHandle);
                if (window != IntPtr.Zero && window != foreground && !IsChild(foreground, window)) return null;
                object password = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                bool? blocked = null;
                if (password is bool && (bool)password) blocked = true;
                else
                {
                    // A browser popup can focus a read-only document over a password field; that blocks too.
                    object pattern;
                    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
                        blocked = ((ValuePattern)pattern).Current.IsReadOnly;
                    else if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                    {
                        object readOnly = ((TextPattern)pattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                        if (readOnly is bool) blocked = (bool)readOnly;
                    }
                }
                // Browser fields share one window; the verdict only counts if focus did not move meanwhile.
                stale = !element.Current.HasKeyboardFocus || !SameFocus(foreground, focus);
                return stale ? null : blocked;
            }
            catch (ElementNotAvailableException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (COMException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        internal static bool SameFocus(IntPtr foreground, IntPtr focus)
        {
            return foreground != IntPtr.Zero && focus != IntPtr.Zero
                && foreground == KeyboardMonitor.GetForegroundWindow() && FocusWindow(foreground) == focus;
        }

        private static IntPtr FocusWindow(IntPtr foreground)
        {
            uint process;
            uint thread = GetWindowThreadProcessId(foreground, out process);
            var info = new GuiInfo { Size = Marshal.SizeOf(typeof(GuiInfo)) };
            return thread != 0 && GetGUIThreadInfo(thread, ref info) && (info.Flags & 0x1e) == 0
                ? info.Focus : IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiInfo
        {
            internal int Size, Flags;
            internal IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            internal int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    }
}
