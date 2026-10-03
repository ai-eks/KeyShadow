using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace keyshadow
{
    // Only current key transitions are passed to the overlay. No text, files or history are collected.
    internal sealed class KeyboardMonitor : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        private readonly HookProc callback;
        private readonly Action<int, bool> changed;
        private IntPtr hook;

        internal KeyboardMonitor(Action<int, bool> onChanged)
        {
            changed = onChanged;
            callback = OnKey;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int msg = message.ToInt32();
                int flags = Marshal.ReadInt32(data, 8);
                if ((flags & 0x10) == 0 && (msg == 0x100 || msg == 0x104 || msg == 0x101 || msg == 0x105))
                    changed(Marshal.ReadInt32(data), msg == 0x100 || msg == 0x104);
            }
            return CallNextHookEx(hook, code, message, data);
        }

        internal static bool ModifiersDown()
        {
            return (GetAsyncKeyState(0x10) & 0x8000) != 0 || (GetAsyncKeyState(0x11) & 0x8000) != 0
                || (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x5b) & 0x8000) != 0
                || (GetAsyncKeyState(0x5c) & 0x8000) != 0;
        }

        // Shift, Ctrl, Alt, Windows and Caps Lock alone neither type nor end an input.
        internal static bool IsModifierKey(int key)
        {
            return (key >= 0x10 && key <= 0x12) || key == 0x14 || key == 0x5b || key == 0x5c || (key >= 0xa0 && key <= 0xa5);
        }

        // Ctrl+Alt+F8 / F9 belong to the registered hotkeys, not to the typed input.
        internal static bool IsHotKey(int key)
        {
            return (key == 0x77 || key == 0x78) && (GetAsyncKeyState(0x11) & 0x8000) != 0 && (GetAsyncKeyState(0x12) & 0x8000) != 0;
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    }

    // Only the screen point of a finished left click is passed on, to estimate where typing happens.
    internal sealed class MouseMonitor : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        private readonly HookProc callback;
        private readonly Action<Point> clicked;
        private IntPtr hook;

        internal MouseMonitor(Action<Point> onClick)
        {
            clicked = onClick;
            callback = OnMouse;
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && message.ToInt32() == 0x202) // WM_LBUTTONUP
                clicked(new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4)));
            return CallNextHookEx(hook, code, message, data);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            GC.KeepAlive(callback);
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    }
}
