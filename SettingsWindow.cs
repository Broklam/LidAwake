using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LidAwake
{
    internal sealed class SettingsWindow : Form
    {
        private readonly AppSettings settings;
        private readonly Version version = typeof(SettingsWindow).Assembly.GetName().Version;
        private readonly CheckBox startup;
        private readonly TextBox repository;
        private readonly Label updateStatus;
        private readonly Button checkUpdates;
        private readonly LinkLabel releaseLink;
        private string savedRepository = "";
        private CancellationTokenSource pendingCheck;
        private Uri releasePage;

        internal SettingsWindow(AppSettings settings, Icon icon)
        {
            this.settings = settings;
            Text = "Lid Awake Settings";
            Icon = icon;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(482, 332);

            var startupGroup = new GroupBox { Text = "Startup", Location = new Point(16, 14), Size = new Size(450, 78) };
            startup = new CheckBox { Text = "Start Lid Awake when I sign in to Windows", AutoSize = true, Location = new Point(15, 24) };
            var startupHint = new Label
            {
                Text = "The app starts in the tray with the lid override off.",
                Bounds = new Rectangle(15, 49, 418, 20),
                ForeColor = SystemColors.GrayText
            };
            startupGroup.Controls.AddRange(new Control[] { startup, startupHint });

            var updatesGroup = new GroupBox { Text = "Updates", Location = new Point(16, 104), Size = new Size(450, 176) };
            var sourceLabel = new Label { Text = "GitHub &repository", AutoSize = true, Location = new Point(15, 25) };
            var versionLabel = new Label
            {
                Text = "Version " + version.ToString(3),
                Bounds = new Rectangle(298, 23, 136, 20),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = SystemColors.GrayText
            };
            repository = new TextBox { Bounds = new Rectangle(15, 46, 419, 23), MaxLength = 250, AccessibleName = "GitHub repository" };
            checkUpdates = new Button { Text = "Check for &updates", Bounds = new Rectangle(15, 79, 132, 28) };
            releaseLink = new LinkLabel { Text = "View release on GitHub", AutoSize = true, Location = new Point(160, 86), Visible = false };
            updateStatus = new Label
            {
                Bounds = new Rectangle(15, 117, 419, 49),
                UseMnemonic = false,
                ForeColor = SystemColors.GrayText
            };
            updatesGroup.Controls.AddRange(new Control[] { sourceLabel, versionLabel, repository, checkUpdates, releaseLink, updateStatus });

            var save = new Button { Text = "&Save", Bounds = new Rectangle(302, 293, 77, 28) };
            var cancel = new Button { Text = "Cancel", Bounds = new Rectangle(389, 293, 77, 28), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { startupGroup, updatesGroup, save, cancel });
            AcceptButton = save;
            CancelButton = cancel;

            try { startup.Checked = settings.StartsWithWindows(); }
            catch (Exception)
            {
                startup.Enabled = false;
                startupHint.Text = "Windows startup settings are unavailable.";
            }
            try
            {
                savedRepository = settings.LoadRepository();
                repository.Text = savedRepository;
                ResetUpdateStatus();
            }
            catch (Exception)
            {
                updateStatus.Text = "The saved GitHub repository could not be read.";
            }

            repository.TextChanged += (sender, args) => ResetUpdateStatus();
            checkUpdates.Click += CheckForUpdates;
            releaseLink.LinkClicked += OpenRelease;
            save.Click += SaveSettings;
            cancel.Click += (sender, args) => Close();
        }

        private void ResetUpdateStatus()
        {
            releasePage = null;
            releaseLink.Visible = false;
            updateStatus.Text = string.IsNullOrWhiteSpace(repository.Text)
                ? "Enter the public repository that publishes Lid Awake releases (owner/repo or a GitHub URL)."
                : "";
        }

        private async void CheckForUpdates(object sender, EventArgs args)
        {
            string source;
            ResetUpdateStatus();
            if (!GitHubReleases.TryNormalizeRepository(repository.Text, out source))
            {
                updateStatus.Text = "Enter a public GitHub repository as owner/repo or a github.com URL.";
                repository.Focus();
                return;
            }

            pendingCheck = new CancellationTokenSource();
            using (var request = pendingCheck)
            {
                checkUpdates.Enabled = false;
                repository.ReadOnly = true;
                updateStatus.Text = "Checking GitHub for the latest release…";
                try
                {
                    GitHubReleases.Result result = await GitHubReleases.CheckAsync(source, version, request.Token);
                    if (!request.IsCancellationRequested && !IsDisposed)
                    {
                        updateStatus.Text = result.Message;
                        releasePage = result.Page;
                        releaseLink.Visible = releasePage != null;
                    }
                }
                catch (TaskCanceledException)
                {
                    if (!request.IsCancellationRequested && !IsDisposed)
                        updateStatus.Text = "The update check timed out. Try again.";
                }
                catch (HttpRequestException)
                {
                    if (!request.IsCancellationRequested && !IsDisposed)
                        updateStatus.Text = "Couldn't reach GitHub. Check your connection and try again.";
                }
                catch (SerializationException)
                {
                    if (!request.IsCancellationRequested && !IsDisposed)
                        updateStatus.Text = "GitHub returned a release that couldn't be read.";
                }
                finally
                {
                    pendingCheck = null;
                    if (!IsDisposed)
                    {
                        repository.ReadOnly = false;
                        checkUpdates.Enabled = true;
                    }
                }
            }
        }

        private void OpenRelease(object sender, LinkLabelLinkClickedEventArgs args)
        {
            if (releasePage == null) return;
            try { Process.Start(new ProcessStartInfo(releasePage.AbsoluteUri) { UseShellExecute = true }); }
            catch (Win32Exception)
            {
                updateStatus.Text = "Windows couldn't open the release page in your browser.";
            }
        }

        private void SaveSettings(object sender, EventArgs args)
        {
            string source = "";
            if (!string.IsNullOrWhiteSpace(repository.Text) && !GitHubReleases.TryNormalizeRepository(repository.Text, out source))
            {
                updateStatus.Text = "Enter a public GitHub repository as owner/repo or a github.com URL.";
                repository.Focus();
                return;
            }
            try
            {
                if (!string.Equals(source, savedRepository, StringComparison.Ordinal))
                {
                    settings.SaveRepository(source);
                    savedRepository = source;
                }
                if (startup.Enabled && (startup.Checked || settings.StartsWithWindows()))
                    settings.SetStartsWithWindows(startup.Checked);
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
            if (pendingCheck != null) pendingCheck.Cancel();
            base.OnFormClosed(e);
        }
    }
}
