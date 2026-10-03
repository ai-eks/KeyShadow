using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using keyshadow;

internal static class StartupTests
{
    private static int checks;

    [STAThread]
    private static int Main()
    {
        string testKey = @"Software\keyshadow.StartupTests." + Guid.NewGuid().ToString("N");
        string executable = @"C:\Tools\键影 测试\键影.exe";
        try
        {
            Check(!StartupRegistration.IsEnabled(null, executable), "missing startup key is disabled");
            StartupRegistration.SetEnabled(null, false, executable);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(testKey))
            {
                key.SetValue("UnrelatedApp", "keep");
                Check(!StartupRegistration.IsEnabled(key, executable), "startup is opt-in");
                StartupRegistration.SetEnabled(key, true, executable);
                Check((string)key.GetValue(StartupRegistration.ValueName) == "\"" + executable + "\" --startup",
                    "Chinese and spaced paths are quoted with silent-start argument");
                Check(key.GetValueKind(StartupRegistration.ValueName) == RegistryValueKind.String,
                    "startup uses a plain string command");
                Check(StartupRegistration.IsEnabled(key, executable.ToUpperInvariant()), "path comparison ignores Windows case");
                StartupRegistration.SetEnabled(key, true, executable);
                Check(key.ValueCount == 2, "enabling twice creates no duplicate entry");
            }
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(testKey))
            {
                Check(StartupRegistration.IsEnabled(key, executable), "startup survives reopening the registry");
                bool denied = false;
                try { StartupRegistration.SetEnabled(key, false, executable); }
                catch (UnauthorizedAccessException) { denied = true; }
                Check(denied && StartupRegistration.IsEnabled(key, executable), "write failure preserves previous setting");
            }
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(testKey, true))
            {
                string moved = @"D:\Apps\键影.exe";
                Check(!StartupRegistration.IsEnabled(key, moved), "old location is not reported as current startup");
                StartupRegistration.SetEnabled(key, true, moved);
                Check(StartupRegistration.IsEnabled(key, moved) && key.ValueCount == 2,
                    "enabling from a moved copy replaces the old path");
                string longest = @"C:\" + new string('x', 245);
                StartupRegistration.SetEnabled(key, true, longest);
                Check(((string)key.GetValue(StartupRegistration.ValueName)).Length == 260, "Run command length boundary is accepted");
                bool tooLong = false;
                try { StartupRegistration.SetEnabled(key, true, longest + "x"); }
                catch (InvalidOperationException) { tooLong = true; }
                Check(tooLong && StartupRegistration.IsEnabled(key, longest), "overlong path is rejected before changing registry");
                StartupRegistration.SetEnabled(key, false, longest);
                StartupRegistration.SetEnabled(key, false, longest);
                Check(!StartupRegistration.IsEnabled(key, longest) && key.GetValue(StartupRegistration.ValueName) == null,
                    "disabling removes the startup entry and can be repeated");
                Check((string)key.GetValue("UnrelatedApp") == "keep" && key.ValueCount == 1,
                    "other applications' entries remain untouched");
            }
            bool first;
            using (var mutex = new Mutex(true, "Local\\keyshadow.v1", out first))
            using (var sink = new MessageSink())
            {
                if (!first) throw new Exception("Exit the running keyboard before desktop tests.");
                IntPtr handle = sink.Handle;
                MethodInfo main = typeof(Program).GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic);
                main.Invoke(null, new object[] { new[] { "--startup" } });
                Pump();
                Check(sink.Reveals == 0, "duplicate startup does not reveal an existing keyboard");
                main.Invoke(null, new object[] { new string[0] });
                Pump();
                Check(sink.Reveals == 1, "ordinary second launch still reveals the keyboard");
            }
            Console.WriteLine("SUCCESS " + checks + " startup checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testKey, false); }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++; Console.WriteLine("PASS " + label);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(10); }
    }

    private sealed class MessageSink : Form
    {
        internal int Reveals;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Program.ShowMessage) { Reveals++; return; }
            base.WndProc(ref message);
        }
    }
}
