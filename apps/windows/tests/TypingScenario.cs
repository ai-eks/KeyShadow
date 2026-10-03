using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using keyshadow;

internal static class TypingScenario
{
    private static KeyboardForm keyboard;
    private static Process fixture;
    private static readonly Timer timer = new Timer { Interval = 50 };
    private static readonly InputLocator locator = new InputLocator();
    private static DateTime started, phaseStarted;
    private static int phase, checks, failures;
    private static InputAnchor anchor;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 1 && args[0] == "--fixture")
        {
            using (var form = new Form { Text = "keyshadow typing verification", ClientSize = new Size(380, 140),
                StartPosition = FormStartPosition.Manual, Location = new Point(80, 80) })
            {
                var edit = new TextBox { Location = new Point(20, 20), Width = 320 };
                form.Controls.Add(edit);
                form.Shown += delegate { form.Activate(); edit.Focus(); };
                Application.Run(form);
            }
            return 0;
        }

        bool hadSettings = File.Exists(Preferences.PathName);
        byte[] originalSettings = hadSettings ? File.ReadAllBytes(Preferences.PathName) : null;
        try
        {
            // Timing/placement scenarios are independent of the desktop's active IME.
            // ImeTests exercises the real cross-process input method reader separately.
            using (keyboard = new KeyboardForm(new Preferences { FollowInput = true, Size = .5f }, delegate { return true; }))
            {
                keyboard.SetPassThrough(true);
                started = phaseStarted = DateTime.UtcNow;
                timer.Tick += Tick;
                timer.Start();
                Application.Run(keyboard);
            }
        }
        catch (Exception error) { failures++; Console.WriteLine("ERROR " + error); }
        finally
        {
            timer.Stop(); timer.Dispose();
            if (fixture != null)
            {
                if (!fixture.HasExited)
                {
                    fixture.CloseMainWindow();
                    if (!fixture.WaitForExit(3000)) { fixture.Kill(); fixture.WaitForExit(); }
                }
                fixture.Dispose();
            }
            if (hadSettings) File.WriteAllBytes(Preferences.PathName, originalSettings);
            else if (File.Exists(Preferences.PathName)) File.Delete(Preferences.PathName);
        }
        bool restored = hadSettings == File.Exists(Preferences.PathName)
            && (!hadSettings || Convert.ToBase64String(originalSettings) == Convert.ToBase64String(File.ReadAllBytes(Preferences.PathName)));
        if (!restored) failures++;
        Console.WriteLine("SettingsRestored=" + restored + " Checks=" + checks + " Failures=" + failures);
        return failures == 0 && checks == 16 ? 0 : 1;
    }

    private static void Key(int value)
    {
        keyboard.OnGlobalKey(value, true);
        keyboard.OnGlobalKey(value, false);
    }

    private static void Check(string name, bool okay, string details)
    {
        checks++;
        if (!okay) failures++;
        Console.WriteLine((okay ? "PASS " : "FAIL ") + name + " " + details);
    }

    private static void Next() { phase++; phaseStarted = DateTime.UtcNow; }

    private static void Tick(object sender, EventArgs e)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            if (now - started > TimeSpan.FromSeconds(30))
            {
                failures++; Console.WriteLine("FAIL watchdog phase=" + phase); keyboard.Close(); return;
            }
            double elapsed = (now - phaseStarted).TotalMilliseconds;
            if (phase == 0)
            {
                if (elapsed < 400) return;
                Check("automatic startup waits for typing", !keyboard.Visible, "visible=" + keyboard.Visible);
                fixture = Process.Start(new ProcessStartInfo {
                    FileName = Assembly.GetExecutingAssembly().Location, Arguments = "--fixture",
                    UseShellExecute = false, CreateNoWindow = true });
                Next();
            }
            else if (phase == 1)
            {
                if (elapsed < 1000) return;
                uint activeProcess;
                GetWindowThreadProcessId(KeyboardMonitor.GetForegroundWindow(), out activeProcess);
                if (activeProcess != fixture.Id || !locator.TryGetNative(out anchor))
                {
                    fixture.Refresh();
                    if (fixture.MainWindowHandle != IntPtr.Zero) SetForegroundWindow(fixture.MainWindowHandle);
                    return;
                }
                Check("focus alone stays hidden", !keyboard.Visible, "visible=" + keyboard.Visible + " nativeCaret=" + anchor.Bounds);
                Key(65); Next();
            }
            else if (phase == 2)
            {
                if (elapsed < 350) return;
                uint activeProcess;
                GetWindowThreadProcessId(KeyboardMonitor.GetForegroundWindow(), out activeProcess);
                bool found = locator.TryGetNative(out anchor);
                bool inScreen = found && Screen.FromRectangle(anchor.Bounds).WorkingArea.Contains(keyboard.Bounds);
                int gap = !found ? -1 : keyboard.Top >= anchor.Bounds.Bottom
                    ? keyboard.Top - anchor.Bounds.Bottom : anchor.Bounds.Top - keyboard.Bottom;
                bool adjacent = found && gap >= 16 && gap <= 260 && keyboard.Left <= anchor.Bounds.Right && keyboard.Right >= anchor.Bounds.Left;
                Check("typing shows by caret without stealing focus", keyboard.Visible && found && inScreen && adjacent && activeProcess == fixture.Id,
                    "visible=" + keyboard.Visible + " inScreen=" + inScreen + " adjacent=" + adjacent + " focusPreserved=" + (activeProcess == fixture.Id));
                string pending = (string)typeof(KeyboardForm).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                Check("first key survives show", pending == "a", "pending=" + pending);
                Key(32); Next();
            }
            else if (phase == 3)
            {
                if (elapsed < 750) return;
                bool hiddenBefore = !keyboard.Visible;
                keyboard.UpdateInputAnchor(anchor);
                Check("space hides and stale anchor cannot reopen", hiddenBefore && !keyboard.Visible, "hiddenBefore=" + hiddenBefore + " visibleAfterStale=" + keyboard.Visible);
                Key(85); Next();
            }
            else if (phase == 4)
            {
                if (elapsed < 350) return;
                Check("new typing opens again", keyboard.Visible, "visible=" + keyboard.Visible);
                Next();
            }
            else if (phase == 5)
            {
                if (elapsed < 2300) return;
                Check("typing timeout hides", !keyboard.Visible, "visible=" + keyboard.Visible);
                Key(76); Next();
            }
            else if (phase == 6)
            {
                if (elapsed < 350) return;
                string syllable = (string)typeof(KeyboardForm).GetField("syllable", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(keyboard);
                Check("syllable resumes after timeout", keyboard.Visible && syllable == "shuang", "visible=" + keyboard.Visible + " syllable=" + syllable);
                Key(32); Next();
            }
            else if (phase == 7)
            {
                if (elapsed < 350) return;
                Key(8); Next();
            }
            else if (phase == 8)
            {
                if (elapsed < 350) return;
                Check("idle backspace does not open", !keyboard.Visible, "visible=" + keyboard.Visible);
                Key(65); Next();
            }
            else if (phase == 9)
            {
                if (elapsed < 350) return;
                keyboard.HideManually();
                keyboard.UpdateInputAnchor(anchor);
                Check("automatic hide waits for the next typed key", !keyboard.Visible, "visible=" + keyboard.Visible);
                Key(65); Next();
            }
            else if (phase == 10)
            {
                if (elapsed < 350) return;
                Check("new typing resumes automatic display after hide", keyboard.Visible, "visible=" + keyboard.Visible);
                keyboard.SetFollowInput(false); Key(32); Next();
            }
            else if (phase == 11)
            {
                if (elapsed < 350) return;
                Check("fixed mode also hides on commit", !keyboard.Visible, "visible=" + keyboard.Visible);
                keyboard.SetFollowInput(true);
                keyboard.ChangeAutoHideDelay(1);
                Key(85); Next();
            }
            else if (phase == 12)
            {
                if (elapsed < 350) return;
                Check("configured one-second session shows", keyboard.Visible, "visible=" + keyboard.Visible);
                Next();
            }
            else if (phase == 13)
            {
                if (elapsed < 1100) return;
                Check("configured one-second timeout hides", !keyboard.Visible, "visible=" + keyboard.Visible);
                keyboard.ChangeAutoHideDelay(0);
                Key(76); Next();
            }
            else if (phase == 14)
            {
                if (elapsed < 2700) return;
                Check("unlimited delay stays visible past original timeout", keyboard.Visible, "visible=" + keyboard.Visible);
                keyboard.ChangeAutoHideDelay(1); Next();
            }
            else if (phase == 15)
            {
                if (elapsed < 1400) return;
                Check("finite delay replaces unlimited active session", !keyboard.Visible, "visible=" + keyboard.Visible);
                keyboard.Close();
            }
        }
        catch (Exception error) { failures++; Console.WriteLine("ERROR " + error); keyboard.Close(); }
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
