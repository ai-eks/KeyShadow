using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using keyshadow;

// Own empty fields only. Chinese mode is forced so an IME's English switch cannot mask a leak.
internal static class PasswordTests
{
    private const int FocusField = 0x8100;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--native")
        {
            Application.EnableVisualStyles();
            using (var fixture = new NativeFixture()) Application.Run(fixture);
            return 0;
        }
        if (args.Length == 1 && args[0] == "--wpf")
        {
            new System.Windows.Application().Run(new WpfFixture());
            return 0;
        }
        Application.EnableVisualStyles();
        byte[] settings = File.Exists(Preferences.PathName) ? File.ReadAllBytes(Preferences.PathName) : null;
        try
        {
            foreach (string kind in new[] { "native", "wpf" })
            using (var fixture = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--" + kind)
                { UseShellExecute = false, CreateNoWindow = true }))
            {
                try
                {
                    Wait(delegate { fixture.Refresh(); return fixture.MainWindowHandle != IntPtr.Zero; }, "fixture ready");
                    IntPtr target = fixture.MainWindowHandle;
                    SetForegroundWindow(target);
                    Wait(delegate { return KeyboardMonitor.GetForegroundWindow() == target; }, "fixture foreground");
                    foreach (bool automatic in new[] { false, true })
                    foreach (bool compact in new[] { false, true })
                    using (var keyboard = new KeyboardForm(new Preferences { FollowInput = automatic, Compact = compact,
                        X = 600, Y = 150, Size = .5f, AutoHideSeconds = 0 }, delegate { return true; }))
                    {
                        string mode = kind + "/" + automatic + "/" + compact;
                        Focus(target, false);
                        keyboard.SetPassThrough(true); keyboard.Show();
                        Wait(delegate { return Field(keyboard, "monitor") != null; }, "keyboard initialized");
                        ((KeyboardMonitor)Field(keyboard, "monitor")).Dispose();
                        Pump(350);
                        Key(keyboard, 85);
                        Wait(delegate { return keyboard.Visible; }, "ordinary input shows: " + mode);
                        Point position = keyboard.Location;
                        Check(Field(keyboard, "pending").Equals("u"), "ordinary first key survives privacy check: " + mode);

                        Focus(target, true);
                        Wait(delegate { return !keyboard.Visible && Cleared(keyboard); }, "password focus hides and clears without a typed key: " + mode);
                        Check(Cleared(keyboard), "password focus clears hints and highlights: " + mode);
                        Key(keyboard, 85); Key(keyboard, 76);
                        Check(!keyboard.Visible, "password keys never synchronously display: " + mode);
                        Pump(800);
                        Check(!keyboard.Visible && Cleared(keyboard), "password keys cannot revive a fixed or automatic overlay: " + mode);
                        Native.PostMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero); Pump(400);
                        Check(!keyboard.Visible && Cleared(keyboard), "manual preview cannot bypass password protection: " + mode);

                        Focus(target, false); Pump(300);
                        Check(!keyboard.Visible, "leaving a password waits for new input: " + mode);
                        Key(keyboard, 85);
                        Wait(delegate { return keyboard.Visible; }, "normal input resumes: " + mode);
                        Check(Field(keyboard, "pending").Equals("u") && (automatic || keyboard.Location == position),
                            "normal input resumes cleanly and preserves fixed position: " + mode);

                        // Same HWND for WPF fields: an old safe verdict must not expose the next field's key.
                        Focus(target, true); Key(keyboard, 65);
                        Check(!keyboard.Visible, "immediate key after focus change is held until inspection: " + mode);
                        Pump(650);
                        Check(!keyboard.Visible && Cleared(keyboard), "late callbacks cannot reveal password keys: " + mode);
                        keyboard.Close();
                    }
                }
                finally
                {
                    if (!fixture.HasExited)
                    {
                        fixture.CloseMainWindow();
                        if (!fixture.WaitForExit(3000)) { fixture.Kill(); fixture.WaitForExit(); }
                    }
                }
            }
            Console.WriteLine("SUCCESS " + checks + " password checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (settings != null) File.WriteAllBytes(Preferences.PathName, settings);
            else if (File.Exists(Preferences.PathName)) File.Delete(Preferences.PathName);
        }
    }

    private static object Field(KeyboardForm form, string name)
    { return typeof(KeyboardForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    private static bool Cleared(KeyboardForm form)
    {
        return Field(form, "pending").Equals("") && Field(form, "lastPair").Equals("") && Field(form, "syllable").Equals("")
            && ((ICollection)Field(form, "flashes")).Count == 0
            && ((System.Collections.Generic.HashSet<int>)Field(form, "pressed")).Count == 0;
    }
    private static void Key(KeyboardForm form, int key) { form.OnGlobalKey(key, true); form.OnGlobalKey(key, false); }
    private static void Check(bool okay, string name)
    { if (!okay) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static void Wait(Func<bool> condition, string name)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > 4000) throw new Exception("Timed out: " + name);
            Pump(20);
        }
    }
    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(10); } while (watch.ElapsedMilliseconds < milliseconds);
    }
    private static void Focus(IntPtr window, bool password)
    {
        UIntPtr result;
        if (SendMessageTimeout(window, FocusField, new IntPtr(password ? 1 : 0), IntPtr.Zero, 0x23, 500, out result) == IntPtr.Zero
            || result == UIntPtr.Zero) throw new Exception("Fixture focus command failed");
    }

    private sealed class NativeFixture : Form
    {
        private readonly TextBox normal = new TextBox { Location = new Point(20, 20), Width = 280 };
        private readonly TextBox password = new TextBox { Location = new Point(20, 70), Width = 280, UseSystemPasswordChar = true };
        internal NativeFixture()
        {
            Text = "KeyShadow password verification";
            StartPosition = FormStartPosition.Manual; Location = new Point(80, 80); ClientSize = new Size(380, 160);
            Controls.Add(normal); Controls.Add(password);
            Shown += delegate { Activate(); normal.Focus(); };
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == FocusField)
            { message.Result = (message.WParam == IntPtr.Zero ? normal : password).Focus() ? new IntPtr(1) : IntPtr.Zero; return; }
            base.WndProc(ref message);
        }
    }

    private sealed class WpfFixture : System.Windows.Window
    {
        private readonly System.Windows.Controls.TextBox normal = new System.Windows.Controls.TextBox { Height = 28 };
        private readonly System.Windows.Controls.PasswordBox password = new System.Windows.Controls.PasswordBox { Height = 28 };
        internal WpfFixture()
        {
            Title = "KeyShadow virtual password verification"; Width = 400; Height = 220; Left = 80; Top = 80;
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
            var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(20) };
            panel.Children.Add(normal); panel.Children.Add(password); Content = panel;
            SourceInitialized += delegate {
                var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                source.AddHook(delegate(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) {
                    if (message != FocusField) return IntPtr.Zero;
                    handled = true;
                    System.Windows.UIElement field = w == IntPtr.Zero ? (System.Windows.UIElement)normal : password;
                    return field.Focus() ? new IntPtr(1) : IntPtr.Zero;
                });
            };
            ContentRendered += delegate { Activate(); normal.Focus(); };
        }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(
        IntPtr window, int message, IntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
}
