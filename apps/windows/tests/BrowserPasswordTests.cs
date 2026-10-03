using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using keyshadow;

// Edge uses guest mode, a separate temporary profile and an empty local page, never the user's tabs or credentials.
internal static class BrowserPasswordTests
{
    private static int checks;
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        byte[] settings = File.Exists(Preferences.PathName) ? File.ReadAllBytes(Preferences.PathName) : null;
        Process browser = null;
        Cdp page = null;
        string root = null;
        try
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft\\Edge\\Application\\msedge.exe");
            if (!File.Exists(exe)) throw new Exception("Microsoft Edge is required for -Browser tests.");
            string title = "KeyShadowPrivacy-" + Guid.NewGuid().ToString("N");
            root = Path.Combine(Path.GetTempPath(), title);
            string profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "fixture.html");
            File.WriteAllText(file, "<!doctype html><meta charset=utf-8><title>" + title + "</title>"
                + "<style>body{padding:40px}input{display:block;margin:30px;width:260px;height:28px}</style>"
                + "<label>Ordinary test field<input id=normal autocomplete=off></label>"
                + "<label>Password test field<input id=secret type=password autocomplete=new-password></label>");
            browser = Process.Start(new ProcessStartInfo(exe, "--user-data-dir=\"" + profile
                + "\" --remote-debugging-port=0 --guest --no-first-run --no-default-browser-check --disable-background-mode --new-window \""
                + new Uri(file).AbsoluteUri + "\"") { UseShellExecute = false });
            string portFile = Path.Combine(profile, "DevToolsActivePort");
            Wait(delegate { return File.Exists(portFile); }, "isolated browser debugging endpoint", 20000);
            string port = File.ReadAllLines(portFile)[0];
            string endpoint = null;
            Wait(delegate {
                using (var client = new WebClient())
                {
                    object[] targets = new JavaScriptSerializer().DeserializeObject(client.DownloadString("http://127.0.0.1:" + port + "/json/list")) as object[];
                    foreach (Dictionary<string, object> target in targets)
                        if (target.ContainsKey("url") && target["url"].ToString() == new Uri(file).AbsoluteUri)
                            endpoint = target["webSocketDebuggerUrl"].ToString();
                    return endpoint != null;
                }
            }, "local test page", 10000);
            page = new Cdp(endpoint);
            page.Call("Page.bringToFront", new { });
            IntPtr window = IntPtr.Zero;
            Wait(delegate {
                EnumWindows(delegate(IntPtr handle, IntPtr ignored) {
                    var caption = new StringBuilder(512); GetWindowText(handle, caption, caption.Capacity);
                    if (caption.ToString().Contains(title)) window = handle;
                    return true;
                }, IntPtr.Zero);
                return window != IntPtr.Zero;
            }, "test browser window", 10000);
            SetForegroundWindow(window);
            Wait(delegate { return KeyboardMonitor.GetForegroundWindow() == window; }, "browser foreground", 4000);
            page.Window = window;

            foreach (bool automatic in new[] { false, true })
            foreach (bool compact in new[] { false, true })
            using (var keyboard = new KeyboardForm(new Preferences { FollowInput = automatic, Compact = compact,
                X = 600, Y = 150, Size = .5f, AutoHideSeconds = 0 }, delegate { return true; }))
            {
                string mode = automatic + "/" + compact;
                bool passwordTyping = false;
                int passwordShows = 0;
                keyboard.VisibleChanged += delegate { if (passwordTyping && keyboard.Visible) passwordShows++; };
                Focus(page, false); keyboard.SetPassThrough(true); keyboard.Show();
                Wait(delegate { return Field(keyboard, "monitor") != null; }, "keyboard initialized", 4000);
                ((KeyboardMonitor)Field(keyboard, "monitor")).Dispose();
                Pump(700);
                keyboard.OnGlobalKey(85, true);
                Wait(delegate { return keyboard.Visible; }, "ordinary browser input shows: " + mode, 4000);
                Point position = keyboard.Location;
                Check(Field(keyboard, "pending").Equals("u"), "browser first key survives asynchronous inspection: " + mode);
                keyboard.OnGlobalKey(85, true);
                Wait(delegate { return keyboard.Visible; }, "held browser key shows: " + mode, 4000);
                Check(Field(keyboard, "pending").Equals("u"), "privacy checks retain held-key repeat suppression: " + mode);
                keyboard.OnGlobalKey(85, false);
                Key(keyboard, 76);
                Wait(delegate { return keyboard.Visible; }, "second browser key shows: " + mode, 4000);
                Check(Field(keyboard, "syllable").Equals("shuang"), "browser syllable survives repeated privacy checks: " + mode);
                IntPtr beforeWindow, beforeFocus, afterWindow, afterFocus;
                InputPrivacy.ReadNative(out beforeWindow, out beforeFocus);
                Focus(page, true);
                InputPrivacy.ReadNative(out afterWindow, out afterFocus);
                Check(beforeWindow == afterWindow && beforeFocus == afterFocus, "browser fields share native focus handles: " + mode);
                Wait(delegate { return !keyboard.Visible && Cleared(keyboard); }, "password focus clears hints without typing: " + mode, 4000);
                passwordTyping = true;
                Key(keyboard, 85); Key(keyboard, 76);
                Check(!keyboard.Visible, "password typing cannot flash a key before inspection: " + mode);
                Pump(800);
                Check(!keyboard.Visible && Cleared(keyboard) && passwordShows == 0,
                    "web password input remains hidden with no cached hints: " + mode + State(keyboard, window));
                Native.PostMessage(keyboard.Handle, Program.ShowMessage, IntPtr.Zero, IntPtr.Zero); Pump(500);
                Check(!keyboard.Visible && Cleared(keyboard), "manual preview cannot reveal web password hints: " + mode);
                passwordTyping = false;
                Focus(page, false); Pump(300);
                Check(!keyboard.Visible, "leaving web password field waits for typing: " + mode);
                Key(keyboard, 85);
                Wait(delegate { return keyboard.Visible; }, "ordinary web input resumes: " + mode, 4000);
                Check(Field(keyboard, "pending").Equals("u") && (automatic || keyboard.Location == position),
                    "ordinary input resumes with first key and fixed position intact: " + mode);
                Focus(page, true); Key(keyboard, 65);
                passwordTyping = true;
                Check(!keyboard.Visible, "immediate password key invalidates the old safe verdict: " + mode);
                Pump(650);
                Check(!keyboard.Visible && Cleared(keyboard) && passwordShows == 0, "late browser inspection cannot reopen password hints: " + mode
                    + State(keyboard, window));
                keyboard.Close();
            }
            Console.WriteLine("SUCCESS " + checks + " browser password checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (page != null) { page.Dispose(); }
            if (browser != null)
            {
                if (!browser.HasExited)
                {
                    browser.CloseMainWindow();
                    if (!browser.WaitForExit(3000)) { browser.Kill(); browser.WaitForExit(); }
                }
                browser.Dispose();
            }
            if (root != null && Directory.Exists(root))
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { } // Edge may still be releasing its temporary profile files.
                catch (UnauthorizedAccessException) { }
            }
            if (settings != null) File.WriteAllBytes(Preferences.PathName, settings);
            else if (File.Exists(Preferences.PathName)) File.Delete(Preferences.PathName);
        }
    }

    private static void Focus(Cdp page, bool password)
    {
        page.Call("Page.bringToFront", new { }); SetForegroundWindow(page.Window);
        Wait(delegate { return KeyboardMonitor.GetForegroundWindow() == page.Window; }, "browser focus before field switch", 4000);
        page.Call("Runtime.evaluate", new { expression = "document.getElementById('" + (password ? "secret" : "normal") + "').focus()" });
    }
    private static object Field(KeyboardForm form, string name)
    { return typeof(KeyboardForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    private static string State(KeyboardForm form, IntPtr window)
    {
        return " visible=" + form.Visible + " cleared=" + Cleared(form)
            + " foregroundPreserved=" + (KeyboardMonitor.GetForegroundWindow() == window);
    }

    private static bool Cleared(KeyboardForm form)
    {
        return Field(form, "pending").Equals("") && Field(form, "lastPair").Equals("") && Field(form, "syllable").Equals("")
            && ((ICollection)Field(form, "flashes")).Count == 0 && ((HashSet<int>)Field(form, "pressed")).Count == 0;
    }
    private static void Key(KeyboardForm form, int key) { form.OnGlobalKey(key, true); form.OnGlobalKey(key, false); }
    private static void Check(bool okay, string name)
    { if (!okay) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static void Wait(Func<bool> condition, string name, int timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        { if (watch.ElapsedMilliseconds > timeout) throw new Exception("Timed out: " + name); Pump(20); }
    }
    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(10); } while (watch.ElapsedMilliseconds < milliseconds);
    }

    private sealed class Cdp : IDisposable
    {
        internal IntPtr Window;
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private int sequence;
        internal Cdp(string endpoint)
        { using (var timeout = new CancellationTokenSource(5000)) socket.ConnectAsync(new Uri(endpoint), timeout.Token).GetAwaiter().GetResult(); }
        internal void Call(string method, object parameters)
        {
            int id = ++sequence;
            byte[] request = Encoding.UTF8.GetBytes(json.Serialize(new { id = id, method = method, @params = parameters }));
            using (var timeout = new CancellationTokenSource(5000))
            {
                socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, timeout.Token).GetAwaiter().GetResult();
                while (true)
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        var buffer = new byte[4096];
                        result = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).GetAwaiter().GetResult();
                        if (result.MessageType == WebSocketMessageType.Close) throw new Exception("Test browser closed its connection");
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    var response = (Dictionary<string, object>)json.DeserializeObject(Encoding.UTF8.GetString(message.ToArray()));
                    if (!response.ContainsKey("id") || Convert.ToInt32(response["id"]) != id) continue;
                    if (response.ContainsKey("error")) throw new Exception("Browser command failed: " + method);
                    return;
                }
            }
        }
        public void Dispose() { socket.Dispose(); }
    }
    private delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
