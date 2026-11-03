using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    return RegisteredPath(key);
            }
        }

        private static string RegisteredPath(RegistryKey key)
        {
            string folder = key == null ? null : key.GetValue("InstallLocation") as string;
            return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(Path.GetFullPath(folder), "LidAwake.exe");
        }

        private static string DefaultFolder()
        {
            var userProgramFiles = new Guid("5CD7AEE2-2219-4A67-B85D-6C9CE15660CB");
            IntPtr path;
            // Request the path even if Windows has not created the Programs directory yet.
            int result = SHGetKnownFolderPath(ref userProgramFiles, 0x4000, IntPtr.Zero, out path);
            try
            {
                string programs = result == 0 ? Marshal.PtrToStringUni(path) :
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
                return Path.Combine(programs, "Lid Awake");
            }
            finally { Marshal.FreeCoTaskMem(path); }
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);

        private static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Lid Awake.lnk"); }
        }

        internal static bool IsSetup(string path)
        {
            return Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"-Setup(?: \([0-9]+\))?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static bool Install()
        {
            try
            {
                using (var setup = new Mutex(false, Program.UserMutexName() + ".Setup"))
                {
                    if (!TryEnterSetup(setup))
                        throw new InvalidOperationException("Another Lid Awake setup operation is in progress.");
                    try { return InstallWithFolder(); }
                    finally { setup.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show("Setup could not finish.\r\n\r\n" + error.Message, "Lid Awake Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private static bool InstallWithFolder()
        {
            if (AppIsRunning())
                throw new InvalidOperationException("Exit Lid Awake from its tray menu, then run Setup again.");

            string existing = InstallPath;
            string initial = existing == null ? DefaultFolder() : Path.GetDirectoryName(existing);
            using (var dialog = new FolderDialog(initial, existing != null))
            {
                if (dialog.ShowDialog() != DialogResult.OK) return false;
                if (AppIsRunning())
                    throw new InvalidOperationException("Exit Lid Awake from its tray menu, then run Setup again.");

                string destination = Path.Combine(dialog.InstallFolder, "LidAwake.exe");
                string uninstaller = Path.Combine(dialog.InstallFolder, "Uninstall.exe");
                if (!CanInstallOver(destination, typeof(Installer).Assembly.GetName().Version))
                {
                    MessageBox.Show("A newer version of Lid Awake is already installed. Open it from the Start menu.",
                        "Lid Awake Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
                InstallFiles(Application.ExecutablePath, destination, uninstaller, existing != null, () =>
                {
                    CreateShortcut(destination, ShortcutPath);
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
                        WriteRegistration(key, destination, uninstaller, typeof(Installer).Assembly.GetName().Version);
                });
                Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
                return true;
            }
        }

        private static bool TryEnterSetup(Mutex setup)
        {
            try { return setup.WaitOne(0); }
            catch (AbandonedMutexException) { return true; }
        }

        internal static void Uninstall()
        {
            string destination = InstallPath;
            if (destination == null || !string.Equals(Path.GetFullPath(Application.ExecutablePath),
                Path.Combine(Path.GetDirectoryName(destination), "Uninstall.exe"), StringComparison.OrdinalIgnoreCase))
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
                    Process.Start(new ProcessStartInfo(helper, "--finish-uninstall " + Process.GetCurrentProcess().Id + " " + Quote(destination))
                        { UseShellExecute = false, CreateNoWindow = true });
                }
                catch { File.Delete(helper); throw; }
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
                if (args.Length != 3 || !int.TryParse(args[1], out pid))
                    throw new InvalidOperationException("Invalid uninstall request.");
                using (var setup = new Mutex(false, Program.UserMutexName() + ".Setup"))
                {
                    if (!TryEnterSetup(setup))
                        throw new InvalidOperationException("Another Lid Awake setup operation is in progress.");
                    try
                    {
                        string destination = InstallPath;
                        if (!string.Equals(Path.GetFullPath(args[2]), destination, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Invalid uninstall request.");
                        WaitForProcess(pid);
                        if (AppIsRunning()) throw new InvalidOperationException("Exit Lid Awake from its tray menu and try again.");
                        File.Delete(destination);
                        AppSettings.RemoveStartupFor(destination);
                        File.Delete(ShortcutPath);
                        string folder = Path.GetDirectoryName(destination);
                        File.Delete(Path.Combine(folder, "Uninstall.exe"));
                        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                    }
                    finally { setup.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show("Could not remove the installed executable.\r\n\r\n" + error.Message,
                    "Lid Awake", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal static void UpdateRegisteredVersion(string executable, Version version)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey, true))
                if (string.Equals(Path.GetFullPath(executable), RegisteredPath(key), StringComparison.OrdinalIgnoreCase))
                    key.SetValue("DisplayVersion", version.ToString(3));
        }

        internal static bool CanInstallOver(string destination, Version incoming)
        {
            return !File.Exists(destination) || AssemblyName.GetAssemblyName(destination).Version <= incoming;
        }

        internal static void InstallFiles(string source, string destination, string uninstaller,
            bool replaceExisting = true, Action register = null)
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
                if (replaceExisting && File.Exists(destination)) { File.Move(destination, appBackup); appMoved = true; }
                if (replaceExisting && File.Exists(uninstaller)) { File.Move(uninstaller, uninstallBackup); uninstallMoved = true; }
                File.Move(appNew, destination);
                appPlaced = true;
                File.Move(uninstallNew, uninstaller);
                uninstallPlaced = true;
                if (register != null) register();
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

        private sealed class FolderDialog : Form
        {
            private readonly TextBox folder;
            internal string InstallFolder { get; private set; }

            internal FolderDialog(string initial, bool existing)
            {
                Text = "Lid Awake Setup";
                Font = SystemFonts.MessageBoxFont;
                AutoScaleMode = AutoScaleMode.Font;
                AutoScaleDimensions = new SizeF(7F, 15F);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ClientSize = new Size(520, 162);

                var label = new Label { Text = "&Destination folder", AutoSize = true, Location = new Point(16, 17) };
                folder = new TextBox { Text = initial, ReadOnly = existing, Bounds = new Rectangle(16, 42, 394, 24) };
                var browse = new Button { Text = "&Browse…", Enabled = !existing, Bounds = new Rectangle(420, 40, 84, 28) };
                var hint = new Label
                {
                    Text = existing ? "Setup will use your existing installation folder." :
                        "Choose a new or empty folder. Setup adds a Start menu shortcut.",
                    Bounds = new Rectangle(16, 76, 488, 37)
                };
                var install = new Button { Text = "&Install", Bounds = new Rectangle(330, 118, 82, 28) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(422, 118, 82, 28) };
                Controls.AddRange(new Control[] { label, folder, browse, hint, install, cancel });
                AcceptButton = install;
                CancelButton = cancel;
                browse.Click += (sender, args) =>
                {
                    using (var picker = new FolderBrowserDialog())
                    {
                        picker.Description = "Choose the folder where Lid Awake will be installed.";
                        picker.SelectedPath = Directory.Exists(folder.Text) ? folder.Text : Path.GetDirectoryName(initial);
                        if (picker.ShowDialog(this) == DialogResult.OK) folder.Text = picker.SelectedPath;
                    }
                };
                install.Click += (sender, args) =>
                {
                    try
                    {
                        string path = Environment.ExpandEnvironmentVariables(folder.Text.Trim());
                        if (!Regex.IsMatch(path, @"^(?:[A-Za-z]:[\\/]|\\\\[^\\])"))
                            throw new ArgumentException("Enter a full path to the installation folder.");
                        path = Path.GetFullPath(path);
                        if (!existing && Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length != 0)
                            throw new IOException("Choose a new or empty folder for Lid Awake.");
                        InstallFolder = path;
                        DialogResult = DialogResult.OK;
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        folder.Focus();
                    }
                };
            }
        }
    }
}
