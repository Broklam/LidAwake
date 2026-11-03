using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Lid Awake")]
[assembly: AssemblyDescription("Windows tray toggle for the laptop lid close action")]
[assembly: AssemblyProduct("Lid Awake")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace LidAwake
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            bool first;
            string userSid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) userSid = identity.User.Value;
            using (var instance = new Mutex(true, @"Global\LidAwake." + userSid, out first))
            {
                if (!first)
                {
                    if (Array.IndexOf(args, "--startup") < 0)
                        MessageBox.Show("Lid Awake is already running. Look for the laptop icon by the clock or under the tray arrow.",
                            "Lid Awake", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LidAwake");
                var lid = new LidOverride(Path.Combine(dataFolder, "restore.txt"));
                using (var tray = new Tray(lid, new AppSettings(dataFolder, Application.ExecutablePath)))
                    Application.Run(tray);
            }
        }
    }

    internal sealed class Tray : ApplicationContext
    {
        private const string AppName = "Lid Awake";
        private const uint ToggleCommand = 1, HourCommand = 2, IndefiniteCommand = 3, SettingsCommand = 4, ExitCommand = 5;
        private const uint Grayed = 0x0001, Separator = 0x0800;
        private const uint RightButton = 0x0002, NoNotify = 0x0080, ReturnCommand = 0x0100;
        private readonly LidOverride lid;
        private readonly AppSettings settings;
        private readonly TrayWindow owner;
        private readonly NotifyIcon icon;
        private readonly Icon offIcon;
        private readonly Icon onIcon;
        private readonly System.Windows.Forms.Timer timer;
        private SettingsWindow settingsWindow;
        private bool menuOpen;

        internal Tray(LidOverride lid, AppSettings settings)
        {
            this.lid = lid;
            this.settings = settings;
            owner = new TrayWindow(RestoreForShutdown);
            offIcon = LoadIcon("lid-off.ico");
            onIcon = LoadIcon("lid-on.ico");
            icon = new NotifyIcon();
            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += CheckForChanges;
            icon.MouseUp += IconClicked;
            try { lid.Restore(); }
            catch (Exception error) { ShowError("Could not restore the previous lid settings.", error); }
            UpdateIcon();
            icon.Visible = true;
        }

        private string Status
        {
            get
            {
                if (lid.IsOn)
                    return lid.HasTimer ? AppName + ": On (" + Math.Max(1, lid.MinutesLeft) + " min left)" : AppName + ": On";
                return AppName + (lid.NeedsRestore ? ": Restore needed" : ": Off");
            }
        }

        private void UpdateIcon()
        {
            Icon currentIcon = lid.IsOn ? onIcon : offIcon;
            if (icon.Icon != currentIcon) icon.Icon = currentIcon;
            string status = Status;
            if (icon.Text != status) icon.Text = status;
            timer.Enabled = lid.IsOn;
        }

        private void IconClicked(object sender, MouseEventArgs e)
        {
            if (menuOpen || (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)) return;
            CheckForChanges(this, EventArgs.Empty);
            menuOpen = true;
            IntPtr menu = CreatePopupMenu();
            uint command = 0;
            bool restore = lid.IsOn || lid.NeedsRestore;
            try
            {
                if (menu == IntPtr.Zero) return;
                AddItem(menu, Grayed, 0, Status);
                AddItem(menu, Separator, 0, null);
                AddItem(menu, 0, ToggleCommand, lid.NeedsRestore ? "Restore previous lid settings" :
                    lid.IsOn ? "Restore normal now" : "Keep awake with lid closed");
                if (!lid.NeedsRestore)
                {
                    AddItem(menu, 0, HourCommand, !lid.IsOn ? "Keep awake for 1 hour" :
                        lid.HasTimer ? "Restart 1-hour timer" : "Restore normal in 1 hour");
                    if (lid.HasTimer) AddItem(menu, 0, IndefiniteCommand, "Keep awake until turned off");
                }
                AddItem(menu, Separator, 0, null);
                AddItem(menu, 0, SettingsCommand, "Settings...");
                AddItem(menu, 0, ExitCommand, "Exit");
                Point cursor = Cursor.Position;
                SetForegroundWindow(owner.Handle);
                command = TrackPopupMenuEx(menu, RightButton | NoNotify | ReturnCommand,
                    cursor.X, cursor.Y, owner.Handle, IntPtr.Zero);
                // Let the next notification-area menu dismiss normally after this one closes.
                PostMessage(owner.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                if (menu != IntPtr.Zero) DestroyMenu(menu);
                menuOpen = false;
            }

            if (command == ToggleCommand || command == HourCommand || command == IndefiniteCommand)
            {
                bool restoring = command == ToggleCommand && restore;
                try
                {
                    if (restoring) lid.Restore();
                    else if (command == HourCommand) lid.EnableForOneHour();
                    else lid.Enable();
                }
                catch (Exception error)
                {
                    ShowError(restoring ? "Could not restore the previous lid settings." :
                        "Could not turn on Lid Awake.", error);
                }
                finally { UpdateIcon(); }
            }
            else if (command == SettingsCommand)
            {
                ShowSettings();
            }
            else if (command == ExitCommand)
            {
                try { lid.Restore(); }
                catch (Exception error)
                {
                    ShowError("Could not restore the previous lid settings. Lid Awake is still running.", error);
                    UpdateIcon();
                    return;
                }
                if (settingsWindow != null) settingsWindow.Close();
                ExitThread();
            }
        }

        private void ShowSettings()
        {
            if (settingsWindow == null)
            {
                settingsWindow = new SettingsWindow(settings, offIcon);
                settingsWindow.FormClosed += (sender, args) => settingsWindow = null;
                settingsWindow.Show();
            }
            settingsWindow.Activate();
        }

        private void CheckForChanges(object sender, EventArgs e)
        {
            string message = null;
            ToolTipIcon notification = ToolTipIcon.Warning;
            try
            {
                RefreshResult result = lid.Refresh();
                if (result == RefreshResult.TimerExpired)
                {
                    message = "The timer ended. Your previous lid settings are restored.";
                    notification = ToolTipIcon.Info;
                }
                else if (result == RefreshResult.SettingsChanged)
                    message = "Windows changed the lid settings. Lid Awake is off.";
            }
            catch (Exception error)
            {
                if (lid.IsOn) RestoreForShutdown();
                message = "Lid Awake stopped: " + error.Message;
                if (lid.NeedsRestore) message += " Open the tray menu to retry restoring your settings.";
            }
            UpdateIcon();
            if (message != null) icon.ShowBalloonTip(5000, AppName, message, notification);
        }

        private void RestoreForShutdown()
        {
            try { lid.Restore(); }
            catch { /* A saved snapshot will be retried the next time the app opens. */ }
        }

        private static void ShowError(string message, Exception error)
        {
            MessageBox.Show(message + "\r\n\r\n" + error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static Icon LoadIcon(string name)
        {
            using (Stream stream = typeof(Tray).Assembly.GetManifestResourceStream(name))
            using (var source = new Icon(stream, SystemInformation.SmallIconSize))
                return (Icon)source.Clone();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (settingsWindow != null) settingsWindow.Close();
                timer.Dispose();
                icon.Dispose();
                owner.DestroyHandle();
                offIcon.Dispose();
                onIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class TrayWindow : NativeWindow
        {
            private readonly Action endingSession;

            internal TrayWindow(Action endingSession)
            {
                this.endingSession = endingSession;
                CreateHandle(new CreateParams { Caption = AppName });
            }

            protected override void WndProc(ref Message message)
            {
                const int WmEndSession = 0x0016;
                if (message.Msg == WmEndSession && message.WParam != IntPtr.Zero) endingSession();
                base.WndProc(ref message);
            }
        }

        private static void AddItem(IntPtr menu, uint flags, uint command, string label)
        {
            AppendMenu(menu, flags, (UIntPtr)command, label);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr command, string label);
        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
