using System;
using Microsoft.Win32;

namespace keyshadow
{
    internal static class StartupRegistration
    {
        internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "keyshadow";

        internal static bool IsEnabled(string executable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                return IsEnabled(key, executable);
        }

        internal static void SetEnabled(bool enabled, string executable)
        {
            using (RegistryKey key = enabled ? Registry.CurrentUser.CreateSubKey(RunKeyPath)
                : Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                SetEnabled(key, enabled, executable);
        }

        // These overloads also let tests use an isolated key, without touching real startup entries.
        internal static bool IsEnabled(RegistryKey key, string executable)
        {
            return key != null && string.Equals(key.GetValue(ValueName) as string,
                Command(executable), StringComparison.OrdinalIgnoreCase);
        }

        internal static void SetEnabled(RegistryKey key, bool enabled, string executable)
        {
            if (enabled)
            {
                string command = Command(executable);
                if (command.Length > 260)
                    throw new InvalidOperationException("程序路径过长，请移到较短的 Windows 本地目录后再开启。");
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else if (key != null) key.DeleteValue(ValueName, false);
        }

        private static string Command(string executable) { return "\"" + executable + "\" --startup"; }
    }
}
