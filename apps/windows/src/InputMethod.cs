using System;
using System.Runtime.InteropServices;

namespace keyshadow
{
    internal static class InputMethod
    {
        // Query the focused application's IME, not this overlay's input context.
        // Called after the keyboard hook returns; an unresponsive IME has a bounded timeout.
        // With a Chinese keyboard layout, an IME state that cannot be read counts as Chinese,
        // like the macOS input-source check: only a readable English state hides the hints.
        internal static bool IsChinese(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero || foreground != KeyboardMonitor.GetForegroundWindow()
                || (GetKeyState(0x14) & 1) != 0) return false;
            uint process;
            uint thread = GetWindowThreadProcessId(foreground, out process);
            if (thread == 0 || (GetKeyboardLayout(thread).ToInt64() & 0x3ff) != 4) return false;
            var info = new GuiInfo { Size = Marshal.SizeOf(typeof(GuiInfo)) };
            if (!GetGUIThreadInfo(thread, ref info) || info.Focus == IntPtr.Zero) return true;
            IntPtr ime = ImmGetDefaultIMEWnd(info.Focus);
            UIntPtr open, conversion;
            if (ime == IntPtr.Zero || !Query(ime, 5, out open)) return true;
            if (open == UIntPtr.Zero) return false;
            if (!Query(ime, 1, out conversion)) return true;
            var latest = new GuiInfo { Size = info.Size };
            return (conversion.ToUInt64() & 1) != 0 && (conversion.ToUInt64() & 0x100) == 0
                && foreground == KeyboardMonitor.GetForegroundWindow()
                && GetGUIThreadInfo(thread, ref latest) && latest.Focus == info.Focus;
        }

        private static bool Query(IntPtr ime, int command, out UIntPtr result)
        {
            // WM_IME_CONTROL: IMC_GETOPENSTATUS (5), IMC_GETCONVERSIONMODE (1).
            return SendMessageTimeout(ime, 0x283, new IntPtr(command), IntPtr.Zero, 0x23, 40, out result) != IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiInfo
        {
            internal int Size, Flags;
            internal IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            internal int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        [DllImport("user32.dll")] private static extern short GetKeyState(int key);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(
            IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    }
}
