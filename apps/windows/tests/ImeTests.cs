using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using keyshadow;

// Requires Microsoft Pinyin with its default Shift Chinese/English switch and Caps Lock off.
// Only the owned fixture receives test keys; its original IME mode and app settings are restored.
internal static class ImeTests
{
    private const int ToggleIme = 0x8001, MoveCaret = 0x8002, RestoreIme = 0x8003, CaptureIme = 0x8004;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 1 && args[0] == "--fixture")
        {
            using (var form = new Fixture()) Application.Run(form);
            return 0;
        }
        byte[] saved = File.Exists(Preferences.PathName) ? File.ReadAllBytes(Preferences.PathName) : null;
        Process fixture = null;
        try
        {
            fixture = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--fixture")
                { UseShellExecute = false, CreateNoWindow = true });
            Wait(delegate { fixture.Refresh(); return fixture.MainWindowHandle != IntPtr.Zero
                && KeyboardMonitor.GetForegroundWindow() == fixture.MainWindowHandle; }, "fixture focus");
            IntPtr target = fixture.MainWindowHandle;
            Pump(600); Command(target, CaptureIme);
            Console.WriteLine("Initial external Chinese=" + InputMethod.IsChinese(target));
            if (!InputMethod.IsChinese(target))
            {
                Command(target, ToggleIme);
                Wait(delegate { return InputMethod.IsChinese(target); }, "Microsoft Pinyin Chinese mode");
            }
            using (var keyboard = new KeyboardForm(new Preferences { FollowInput = true, Size = .5f, AutoHideSeconds = 10 }))
            {
                keyboard.SetPassThrough(true);
                keyboard.Show(); Pump(350);
                Check(InputMethod.IsChinese(target), "read Chinese mode from another process");
                Check(!keyboard.Visible, "Chinese mode and caret alone do not show on startup");
                Check(!InputMethod.IsChinese(IntPtr.Zero) && !InputMethod.IsChinese(keyboard.Handle), "missing or nonforeground context is rejected");
                Key(keyboard, 85);
                Wait(delegate { return keyboard.Visible; }, "Chinese typing displays overlay");
                Check(KeyboardMonitor.GetForegroundWindow() == target && Pending(keyboard) == "u", "first Chinese key preserves focus and pinyin");
                Point position = keyboard.Location;
                Command(target, MoveCaret); Pump(400);
                Check(keyboard.Location == position, "caret moves during pause without moving overlay");
                Key(keyboard, 76);
                Wait(delegate { return keyboard.Location != position; }, "next typed key requests a new position");
                Check(keyboard.Visible, "continued Chinese typing updates position");
                keyboard.HideManually(); Pump(250);
                Check(!keyboard.Visible, "automatic hide waits for new typing instead of reopening from an old caret");
                Command(target, ToggleIme);
                Wait(delegate { return !InputMethod.IsChinese(target) && !keyboard.Visible; }, "switching to English hides overlay");
                Check(Pending(keyboard) == "", "English switch clears partial pinyin");
                position = keyboard.Location;
                Key(keyboard, 65); Pump(350);
                Check(!keyboard.Visible && keyboard.Location == position, "English typing neither shows nor moves overlay");
                Command(target, ToggleIme);
                Wait(delegate { return InputMethod.IsChinese(target); }, "return to Chinese mode");
                Pump(250);
                Check(!keyboard.Visible && keyboard.Location == position, "Chinese switch alone remains hidden");
                Key(keyboard, 85);
                Wait(delegate { return keyboard.Visible; }, "new Chinese typing resumes display");
                Check(Pending(keyboard) == "u", "new Chinese session starts from its first key");
                keyboard.CloseKeyboard();
                Key(keyboard, 85); Pump(350);
                Check(!keyboard.Visible && Pending(keyboard) == "", "explicit close pauses real Chinese typing");
                Native.PostMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero); Pump(250);
                Check(keyboard.Visible, "explicit restore resumes a closed keyboard");
                keyboard.HideManually(); Key(keyboard, 85);
                Wait(delegate { return keyboard.Visible; }, "collapse resumes on typing after close and restore");
                Check(Pending(keyboard) == "u", "restored automatic collapse still preserves first key");
                Key(keyboard, 32); Pump(250);
                Check(!keyboard.Visible, "commit hides without caret reopening");
                keyboard.SetFollowInput(false); Pump(200);
                Command(target, ToggleIme);
                Wait(delegate { return !InputMethod.IsChinese(target); }, "English mode for fixed display");
                Pump(250);
                Check(!keyboard.Visible, "fixed position also hides when switching to English");
                position = keyboard.Location;
                Key(keyboard, 65); Pump(350);
                Check(!keyboard.Visible && keyboard.Location == position, "English typing cannot reopen a fixed keyboard");
                foreach (bool compact in new[] { false, true })
                {
                    keyboard.SetCompact(compact);
                    Command(target, ToggleIme);
                    Wait(delegate { return InputMethod.IsChinese(target); }, "Chinese mode for fixed typing");
                    Pump(250);
                    Check(!keyboard.Visible, "Chinese switch alone keeps fixed keyboard hidden: " + compact);
                    position = keyboard.Location;
                    Key(keyboard, 85);
                    Wait(delegate { return keyboard.Visible; }, "Chinese typing shows fixed keyboard");
                    Check(keyboard.Location == position && Pending(keyboard) == "u", "fixed typing preserves position and first key: " + compact);
                    Command(target, MoveCaret); Pump(250);
                    Key(keyboard, 76); Pump(250);
                    Check(keyboard.Visible && keyboard.Location == position, "continued typing never moves fixed keyboard: " + compact);
                    keyboard.HideManually(); Pump(250);
                    Check(!keyboard.Visible, "fixed collapse waits for new input: " + compact);
                    Key(keyboard, 85);
                    Wait(delegate { return keyboard.Visible; }, "new Chinese typing restores collapsed fixed keyboard");
                    Check(keyboard.Location == position && Pending(keyboard) == "u", "fixed collapse resumes at the same position with the first key: " + compact);
                    keyboard.CloseKeyboard(); Key(keyboard, 85); Pump(250);
                    Check(!keyboard.Visible && Pending(keyboard) == "", "explicit close still pauses fixed Chinese input: " + compact);
                    Native.PostMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero); Pump(250);
                    Key(keyboard, 85); Pump(250);
                    Command(target, ToggleIme);
                    Wait(delegate { return !InputMethod.IsChinese(target) && !keyboard.Visible; }, "English hides fixed keyboard in both layouts");
                    Check(Pending(keyboard) == "", "English clears fixed keyboard state: " + compact);
                }
                keyboard.SetFollowInput(true); Pump(250);
                Check(!keyboard.Visible, "automatic mode waits for the next Chinese input");
                keyboard.Close();
            }
            Console.WriteLine("SUCCESS " + checks + " real IME checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (fixture != null)
            {
                if (!fixture.HasExited)
                {
                    fixture.Refresh();
                    SetForegroundWindow(fixture.MainWindowHandle); Pump(150);
                    Command(fixture.MainWindowHandle, RestoreIme); Pump(250);
                    fixture.CloseMainWindow(); fixture.WaitForExit(3000);
                }
                fixture.Dispose();
            }
            if (saved != null) File.WriteAllBytes(Preferences.PathName, saved);
            else if (File.Exists(Preferences.PathName)) File.Delete(Preferences.PathName);
        }
    }

    private static void Key(KeyboardForm keyboard, int key) { keyboard.OnGlobalKey(key, true); keyboard.OnGlobalKey(key, false); }
    private static string Pending(KeyboardForm keyboard)
    {
        return (string)typeof(KeyboardForm).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++; Console.WriteLine("PASS " + name);
    }
    private static void Wait(Func<bool> condition, string name)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 3500) throw new Exception("Timed out: " + name);
            Pump(20);
        }
    }
    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(10); } while (watch.ElapsedMilliseconds < milliseconds);
    }
    private static void Command(IntPtr target, int message)
    {
        UIntPtr result;
        if (SendMessageTimeout(target, message, IntPtr.Zero, IntPtr.Zero, 0x23, 500, out result) == IntPtr.Zero
            || result == UIntPtr.Zero) throw new Exception("Fixture rejected command " + message);
    }

    private sealed class Fixture : Form
    {
        private readonly TextBox edit = new TextBox { Location = new Point(20, 20), Width = 220 };
        private bool originalChinese;
        internal Fixture()
        {
            Text = "KeyShadow Microsoft Pinyin verification";
            StartPosition = FormStartPosition.Manual; Location = new Point(80, 80);
            ClientSize = new Size(650, 120); Controls.Add(edit);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e); Activate(); edit.Focus();
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == ToggleIme || message.Msg == RestoreIme || message.Msg == MoveCaret || message.Msg == CaptureIme)
            {
                if (KeyboardMonitor.GetForegroundWindow() != Handle || !edit.Focused || KeyboardMonitor.ModifiersDown())
                { message.Result = IntPtr.Zero; return; }
                if (message.Msg == CaptureIme)
                {
                    originalChinese = InputMethod.IsChinese(Handle);
                    Console.WriteLine("Initial fixture Chinese=" + originalChinese);
                }
                else if (message.Msg == MoveCaret) edit.Left += 240;
                else if (message.Msg == ToggleIme || originalChinese != InputMethod.IsChinese(Handle))
                {
                    keybd_event(0x10, 0x2a, 0, UIntPtr.Zero);
                    keybd_event(0x10, 0x2a, 2, UIntPtr.Zero);
                }
                message.Result = new IntPtr(1); return;
            }
            base.WndProc(ref message);
        }
    }

    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(
        IntPtr window, int message, IntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
}
