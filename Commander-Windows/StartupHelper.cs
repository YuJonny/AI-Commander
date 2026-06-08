using System;
using Microsoft.Win32;

namespace Commander
{
    public static class StartupHelper
    {
        private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Commander_AI接线员";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                    return key?.GetValue(AppName) != null;
                }
                catch { return false; }
            }
        }

        public static void Enable()
        {
            try
            {
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                key?.SetValue(AppName, $"\"{exePath}\"");
            }
            catch { }
        }

        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
                key?.DeleteValue(AppName, false);
            }
            catch { }
        }
    }
}
