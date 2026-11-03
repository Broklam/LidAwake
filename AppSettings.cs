using System;
using System.IO;
using Microsoft.Win32;

namespace LidAwake
{
    internal sealed class AppSettings
    {
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValue = "Lid Awake";
        private readonly string autoUpdateFile;
        private readonly string launchChoiceFile;
        private readonly string executablePath;

        internal AppSettings(string dataFolder, string executablePath)
        {
            autoUpdateFile = Path.Combine(dataFolder, "auto-update.txt");
            launchChoiceFile = Path.Combine(dataFolder, "launch-choice.txt");
            this.executablePath = executablePath;
        }

        internal string LoadLaunchChoice()
        {
            try { return File.ReadAllText(launchChoiceFile).Trim(); }
            catch (FileNotFoundException) { return ""; }
            catch (DirectoryNotFoundException) { return ""; }
        }

        internal void SaveLaunchChoice(string choice)
        {
            if (choice != "standalone" && choice != "installed") throw new ArgumentException("Invalid launch choice.");
            Directory.CreateDirectory(Path.GetDirectoryName(launchChoiceFile));
            File.WriteAllText(launchChoiceFile, choice);
        }

        internal void ClearLaunchChoice()
        {
            if (File.Exists(launchChoiceFile)) File.Delete(launchChoiceFile);
        }

        internal bool AutoUpdatesEnabled()
        {
            try { return string.Equals(File.ReadAllText(autoUpdateFile).Trim(), "true", StringComparison.OrdinalIgnoreCase); }
            catch (FileNotFoundException) { return true; }
            catch (DirectoryNotFoundException) { return true; }
        }

        internal void SetAutoUpdates(bool enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(autoUpdateFile));
            File.WriteAllText(autoUpdateFile, enabled ? "true" : "false");
        }

        internal bool StartsWithWindows()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey))
                return key != null && key.GetValue(StartupValue) != null;
        }

        internal void SetStartsWithWindows(bool enabled)
        {
            if (enabled)
            {
                string command = "\"" + executablePath + "\" --startup";
                if (command.Length > 260)
                    throw new InvalidOperationException("The app path is too long for Windows startup. Move the EXE to a folder with a shorter path.");
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey))
                    key.SetValue(StartupValue, command, RegistryValueKind.String);
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey, writable: true))
                    if (key != null) key.DeleteValue(StartupValue, throwOnMissingValue: false);
            }
        }

        internal static void RemoveStartupFor(string path)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey, writable: true))
            {
                if (key == null) return;
                string command = key.GetValue(StartupValue) as string;
                if (string.Equals(command, "\"" + path + "\" --startup", StringComparison.OrdinalIgnoreCase))
                    key.DeleteValue(StartupValue, throwOnMissingValue: false);
            }
        }
    }
}
