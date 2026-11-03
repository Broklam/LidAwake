using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LidAwake
{
    internal enum LaunchAction { Ask, Standalone, Installed }

    internal static class LaunchChoice
    {
        internal static LaunchAction Resolve(string executable, string[] args, string remembered, bool installationExists)
        {
            if (string.Equals(Path.GetFullPath(executable), Path.GetFullPath(Installer.InstallPath), StringComparison.OrdinalIgnoreCase) ||
                Array.IndexOf(args, "--startup") >= 0 || Array.IndexOf(args, "--update-ready") >= 0 ||
                Array.IndexOf(args, "--update-failed") >= 0)
                return LaunchAction.Standalone;
            if (remembered == "standalone") return LaunchAction.Standalone;
            if (remembered == "installed" && installationExists) return LaunchAction.Installed;
            return LaunchAction.Ask;
        }

        internal static bool ContinueWithStandalone(AppSettings settings, string[] args)
        {
            string remembered = "";
            try { remembered = settings.LoadLaunchChoice(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            LaunchAction action = Resolve(Application.ExecutablePath, args, remembered, File.Exists(Installer.InstallPath));
            if (action == LaunchAction.Standalone) return true;
            if (action == LaunchAction.Installed)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Installer.InstallPath) { UseShellExecute = true });
                    return false;
                }
                catch (Exception error)
                {
                    MessageBox.Show("Couldn't open the installed copy. Choose how to continue.\r\n\r\n" + error.Message,
                        "Lid Awake", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            using (var dialog = new ChoiceDialog())
            {
                if (dialog.ShowDialog() != DialogResult.OK) return false;
                bool standalone = dialog.Standalone;
                if (!standalone && !Installer.Install(false)) return false;
                try
                {
                    if (dialog.Remember) settings.SaveLaunchChoice(standalone ? "standalone" : "installed");
                    else settings.ClearLaunchChoice();
                }
                catch (Exception error)
                {
                    MessageBox.Show("The launch choice could not be saved.\r\n\r\n" + error.Message,
                        "Lid Awake", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return standalone;
            }
        }

        private sealed class ChoiceDialog : Form
        {
            private readonly RadioButton standalone;
            private readonly CheckBox remember;
            internal bool Standalone { get { return standalone.Checked; } }
            internal bool Remember { get { return remember.Checked; } }

            internal ChoiceDialog()
            {
                Text = "Use Lid Awake";
                Font = SystemFonts.MessageBoxFont;
                AutoScaleMode = AutoScaleMode.Font;
                AutoScaleDimensions = new SizeF(7F, 15F);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ClientSize = new Size(440, 212);
                var title = new Label { Text = "How would you like to use Lid Awake?", AutoSize = true, Location = new Point(18, 17) };
                standalone = new RadioButton
                {
                    Text = "Run standalone (no installation)", AutoSize = true,
                    Location = new Point(22, 48), Checked = true
                };
                var install = new RadioButton
                {
                    Text = "Install for this Windows account", AutoSize = true, Location = new Point(22, 78)
                };
                remember = new CheckBox { Text = "Don't ask again", AutoSize = true, Location = new Point(22, 119) };
                var ok = new Button { Text = "Continue", DialogResult = DialogResult.OK, Bounds = new Rectangle(258, 166, 75, 28) };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(344, 166, 75, 28) };
                Controls.AddRange(new Control[] { title, standalone, install, remember, ok, cancel });
                AcceptButton = ok;
                CancelButton = cancel;
            }
        }
    }
}
