using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LidAwake
{
    internal static class Installer
    {
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\LidAwake";

        internal static string InstallPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Lid Awake\LidAwake.exe"); }
        }

        private static string UninstallPath
        {
            get { return Path.Combine(Path.GetDirectoryName(InstallPath), "Uninstall.exe"); }
        }

        private static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lid Awake.lnk"); }
        }

        internal static bool IsSetup(string path)
        {
            return Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"-Setup(?: \([0-9]+\))?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static bool Install(bool confirm)
        {
            if (confirm && MessageBox.Show("Install Lid Awake for this Windows account?\r\n\r\nIt will be available from the Start menu.",
                "Lid Awake Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
            if (AppIsRunning())
            {
                MessageBox.Show("Exit the running copy of Lid Awake from its tray menu, then run Setup again.",
                    "Lid Awake Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            try
            {
                string destination = InstallPath;
                if (!CanInstallOver(destination, typeof(Installer).Assembly.GetName().Version))
                {
                    MessageBox.Show("A newer version of Lid Awake is already installed. Open it from the Start menu.",
                        "Lid Awake Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
                InstallFiles(Application.ExecutablePath, destination, UninstallPath);
                CreateShortcut(destination, ShortcutPath);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                    WriteRegistration(key, destination, UninstallPath, typeof(Installer).Assembly.GetName().Version);
                Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show("Setup could not finish.\r\n\r\n" + error.Message, "Lid Awake Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        internal static void Uninstall()
        {
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(UninstallPath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("This is not the installed copy of Lid Awake.", "Lid Awake",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (AppIsRunning())
            {
                MessageBox.Show("Exit Lid Awake from its tray menu before uninstalling it.", "Lid Awake",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("Uninstall Lid Awake for this Windows account?\r\n\r\nYour saved settings will be kept.",
                "Lid Awake", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                string helper = CopyHelper();
                try
                {
                    Process.Start(new ProcessStartInfo(helper, "--finish-uninstall " + Process.GetCurrentProcess().Id + " " + Quote(InstallPath))
                        { UseShellExecute = false, CreateNoWindow = true });
                }
                catch { File.Delete(helper); throw; }
                AppSettings.RemoveStartupFor(InstallPath);
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            }
            catch (Exception error)
            {
                MessageBox.Show("Uninstall could not finish.\r\n\r\n" + error.Message, "Lid Awake",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal static void FinishUninstall(string[] args)
        {
            try
            {
                int pid;
                if (args.Length != 3 || !int.TryParse(args[1], out pid) ||
                    !string.Equals(Path.GetFullPath(args[2]), Path.GetFullPath(InstallPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid uninstall request.");
                WaitForProcess(pid);
                File.Delete(InstallPath);
                File.Delete(UninstallPath);
                string folder = Path.GetDirectoryName(InstallPath);
                if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
            }
            catch (Exception error)
            {
                MessageBox.Show("Could not remove the installed executable.\r\n\r\n" + error.Message,
                    "Lid Awake", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal static void UpdateRegisteredVersion(string executable, Version version)
        {
            if (!string.Equals(Path.GetFullPath(executable), Path.GetFullPath(InstallPath), StringComparison.OrdinalIgnoreCase)) return;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey, true))
                if (key != null) key.SetValue("DisplayVersion", version.ToString(3));
        }

        internal static bool CanInstallOver(string destination, Version incoming)
        {
            return !File.Exists(destination) || AssemblyName.GetAssemblyName(destination).Version <= incoming;
        }

        internal static void InstallFiles(string source, string destination, string uninstaller)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string suffix = "." + Guid.NewGuid().ToString("N");
            string appNew = destination + suffix + ".new";
            string uninstallNew = uninstaller + suffix + ".new";
            string appBackup = destination + suffix + ".bak";
            string uninstallBackup = uninstaller + suffix + ".bak";
            bool appMoved = false, uninstallMoved = false, appPlaced = false, uninstallPlaced = false;
            try
            {
                File.Copy(source, appNew);
                File.Copy(source, uninstallNew);
                if (File.Exists(destination)) { File.Move(destination, appBackup); appMoved = true; }
                if (File.Exists(uninstaller)) { File.Move(uninstaller, uninstallBackup); uninstallMoved = true; }
                File.Move(appNew, destination);
                appPlaced = true;
                File.Move(uninstallNew, uninstaller);
                uninstallPlaced = true;
            }
            catch (Exception error)
            {
                Exception recovery = null;
                try
                {
                    if (appPlaced) File.Delete(destination);
                    if (appMoved) File.Move(appBackup, destination);
                }
                catch (Exception failed) { recovery = failed; }
                try
                {
                    if (uninstallPlaced) File.Delete(uninstaller);
                    if (uninstallMoved) File.Move(uninstallBackup, uninstaller);
                }
                catch (Exception failed) { if (recovery == null) recovery = failed; }
                if (recovery != null)
                    throw new IOException("Setup failed and couldn't restore a previous file. Recovery backups, if present, are in " +
                        Path.GetDirectoryName(destination) + ". " + recovery.Message, error);
                throw;
            }
            finally
            {
                try { File.Delete(appNew); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { File.Delete(uninstallNew); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            try { if (appMoved) File.Delete(appBackup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { if (uninstallMoved) File.Delete(uninstallBackup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        internal static void WriteRegistration(RegistryKey key, string destination, string uninstaller, Version version)
        {
            foreach (KeyValuePair<string, object> value in RegistrationValues(destination, uninstaller, version))
                key.SetValue(value.Key, value.Value);
        }

        internal static IDictionary<string, object> RegistrationValues(string destination, string uninstaller, Version version)
        {
            return new Dictionary<string, object> {
                { "DisplayName", "Lid Awake" }, { "Publisher", "Broklam" }, { "DisplayVersion", version.ToString(3) },
                { "InstallLocation", Path.GetDirectoryName(destination) }, { "DisplayIcon", destination },
                { "UninstallString", Quote(uninstaller) + " --uninstall" }, { "NoModify", 1 }, { "NoRepair", 1 }
            };
        }

        internal static string CopyHelper()
        {
            string helper = Path.Combine(Path.GetTempPath(), "LidAwake-helper-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, helper);
            return helper;
        }

        internal static void WaitForProcess(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                    if (!process.WaitForExit(30000)) throw new InvalidOperationException("The old Lid Awake process did not exit.");
            }
            catch (ArgumentException) { } // The process has already exited.
        }

        internal static string Quote(string value) { return "\"" + value + "\""; }

        private static bool AppIsRunning()
        {
            try
            {
                using (Mutex existing = Mutex.OpenExisting(Program.UserMutexName())) return true;
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }

        internal static void CreateShortcut(string destination, string shortcutPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell", true);
            object shell = Activator.CreateInstance(shellType);
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type type = shortcut.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { destination });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(destination) });
                type.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Lid Awake" });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
