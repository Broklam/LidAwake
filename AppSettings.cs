using System;
using System.IO;
using Microsoft.Win32;

namespace LidAwake
{
    internal sealed class AppSettings
    {
        private const string DefaultRepository = "Broklam/LidAwake";
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValue = "Lid Awake";
        private readonly string repositoryFile;
        private readonly string executablePath;

        internal AppSettings(string dataFolder, string executablePath)
        {
            repositoryFile = Path.Combine(dataFolder, "update-repository.txt");
            this.executablePath = executablePath;
        }

        internal string LoadRepository()
        {
            try { return File.ReadAllText(repositoryFile).Trim(); }
            catch (FileNotFoundException) { return DefaultRepository; }
            catch (DirectoryNotFoundException) { return DefaultRepository; }
        }

        internal void SaveRepository(string repository)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(repositoryFile));
            File.WriteAllText(repositoryFile, repository);
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
    }
}
