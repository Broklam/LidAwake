using System;
using System.Drawing;
using System.Windows.Forms;

namespace LidAwake
{
    internal sealed class SettingsWindow : Form
    {
        private readonly AppSettings settings;
        private readonly Updates updates;
        private readonly CheckBox startup;
        private readonly CheckBox autoUpdate;
        private readonly Label updateStatus;
        private bool savedAutoUpdate;

        internal SettingsWindow(AppSettings settings, Icon icon, Updates updates)
        {
            this.settings = settings;
            this.updates = updates;
            Text = "Lid Awake Settings";
            Icon = icon;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(482, 297);

            var startupGroup = new GroupBox { Text = "Startup", Location = new Point(16, 14), Size = new Size(450, 78) };
            startup = new CheckBox { Text = "Start Lid Awake when I sign in to Windows", AutoSize = true, Location = new Point(15, 24) };
            var startupHint = new Label
            {
                Text = "The app starts in the tray with the lid override off.",
                Bounds = new Rectangle(15, 49, 418, 20),
                ForeColor = SystemColors.GrayText
            };
            startupGroup.Controls.AddRange(new Control[] { startup, startupHint });

            var updatesGroup = new GroupBox { Text = "Updates", Location = new Point(16, 104), Size = new Size(450, 141) };
            autoUpdate = new CheckBox { Text = "Automatically check for and install updates", AutoSize = true, Location = new Point(15, 25) };
            var versionLabel = new Label
            {
                Text = "Version " + typeof(SettingsWindow).Assembly.GetName().Version.ToString(3),
                Bounds = new Rectangle(298, 50, 136, 20),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = SystemColors.GrayText
            };
            updateStatus = new Label
            {
                Bounds = new Rectangle(15, 75, 419, 56),
                UseMnemonic = false,
                ForeColor = SystemColors.GrayText
            };
            updatesGroup.Controls.AddRange(new Control[] { autoUpdate, versionLabel, updateStatus });

            var save = new Button { Text = "&Save", Bounds = new Rectangle(302, 257, 77, 28) };
            var cancel = new Button { Text = "Cancel", Bounds = new Rectangle(389, 257, 77, 28), DialogResult = DialogResult.Cancel };
            var resetChoice = new Button { Text = "Show install options next time", Bounds = new Rectangle(16, 257, 210, 28) };
            Controls.AddRange(new Control[] { startupGroup, updatesGroup, resetChoice, save, cancel });
            AcceptButton = save;
            CancelButton = cancel;

            try { startup.Checked = settings.StartsWithWindows(); }
            catch (Exception)
            {
                startup.Enabled = false;
                startupHint.Text = "Windows startup settings are unavailable.";
            }
            try { savedAutoUpdate = autoUpdate.Checked = settings.AutoUpdatesEnabled(); }
            catch (Exception)
            {
                autoUpdate.Enabled = false;
                updateStatus.Text = "The automatic update setting could not be read.";
            }

            resetChoice.Click += (sender, args) =>
            {
                try
                {
                    settings.ClearLaunchChoice();
                    updateStatus.Text = "Install options will appear on the next manual standalone launch.";
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, "Couldn't reset the launch choice.\r\n\r\n" + error.Message,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            save.Click += SaveSettings;
            cancel.Click += (sender, args) => Close();
            updates.Changed += UpdateStatus;
            UpdateStatus(this, EventArgs.Empty);
        }

        private void UpdateStatus(object sender, EventArgs args)
        {
            if (IsDisposed || !autoUpdate.Enabled) return;
            updateStatus.Text = string.IsNullOrEmpty(updates.Status)
                ? (savedAutoUpdate ? "Lid Awake restarts when a newer release is ready." : "Automatic updates are off.")
                : updates.Status;
        }

        private void SaveSettings(object sender, EventArgs args)
        {
            try
            {
                if (startup.Enabled && (startup.Checked || settings.StartsWithWindows()))
                    settings.SetStartsWithWindows(startup.Checked);
                if (autoUpdate.Enabled && autoUpdate.Checked != savedAutoUpdate)
                    settings.SetAutoUpdates(autoUpdate.Checked);
                bool wasEnabled = savedAutoUpdate;
                savedAutoUpdate = autoUpdate.Checked;
                updates.SettingsSaved(wasEnabled, savedAutoUpdate);
                Close();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Couldn't save settings.\r\n\r\n" + error.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            updates.Changed -= UpdateStatus;
            base.OnFormClosed(e);
        }
    }
}
