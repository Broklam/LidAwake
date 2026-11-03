using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LidAwake
{
    internal sealed class Updates : IDisposable
    {
        private readonly AppSettings settings;
        private readonly string dataFolder;
        private readonly Func<bool> canApply;
        private readonly Action exitForUpdate;
        private readonly Func<Version, CancellationToken, Task<GitHubReleases.Result>> checkRelease;
        private readonly Func<GitHubReleases.Result, string, CancellationToken, Task> downloadRelease;
        private readonly System.Windows.Forms.Timer timer;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private GitHubReleases.Result pending;
        private string staged;
        private bool pendingManual;
        private bool pendingNeedsCheck;
        private bool activeManual;
        private bool canceledBySettings;
        private CancellationTokenSource activeCheck;
        private bool disposed;
        internal event EventHandler Changed;
        internal bool Busy { get; private set; }
        internal string Status { get; private set; }

        internal Updates(AppSettings settings, string dataFolder, Func<bool> canApply, Action exitForUpdate, bool updateFailed)
            : this(settings, dataFolder, canApply, exitForUpdate, updateFailed, GitHubReleases.CheckAsync, GitHubReleases.DownloadAsync) { }

        internal Updates(AppSettings settings, string dataFolder, Func<bool> canApply, Action exitForUpdate, bool updateFailed,
            Func<Version, CancellationToken, Task<GitHubReleases.Result>> checkRelease,
            Func<GitHubReleases.Result, string, CancellationToken, Task> downloadRelease)
        {
            this.settings = settings;
            this.dataFolder = dataFolder;
            this.canApply = canApply;
            this.exitForUpdate = exitForUpdate;
            this.checkRelease = checkRelease;
            this.downloadRelease = downloadRelease;
            Status = updateFailed ? "The previous update failed. The original version was restored." : "";
            RestorePending(updateFailed);
            timer = new System.Windows.Forms.Timer { Interval = updateFailed ? 21600000 : 10000 };
            timer.Tick += (sender, args) =>
            {
                timer.Interval = 21600000; // Check again every six hours while the app is running.
                if (pending != null) { Check(pendingManual); return; }
                try { if (settings.AutoUpdatesEnabled()) Check(false); }
                catch (Exception error) { SetStatus("Couldn't read the automatic update setting: " + error.Message); }
            };
            timer.Start();
        }

        internal void SettingsSaved(bool wasEnabled, bool isEnabled)
        {
            if (wasEnabled && !isEnabled)
            {
                if (activeCheck != null && !activeManual)
                {
                    canceledBySettings = true;
                    activeCheck.Cancel();
                }
                if (pending != null && !pendingManual)
                { ClearPending(); SetStatus("Automatic update canceled."); }
            }
            if (!wasEnabled && isEnabled) Check(false);
        }

        internal async void Check(bool manual)
        {
            if (Busy || disposed) return;
            if (pending != null && manual && !pendingManual)
            {
                try { SavePending(pending, Path.GetDirectoryName(staged), true); pendingManual = true; }
                catch (Exception error) { SetStatus("Couldn't save the pending update: " + error.Message); return; }
            }
            Busy = true;
            activeManual = manual;
            canceledBySettings = false;
            activeCheck = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            activeCheck.CancelAfter(TimeSpan.FromMinutes(5));
            SetStatus("Checking GitHub for the latest release…");
            string directory = null;
            bool applyAfterCheck = false;
            try
            {
                GitHubReleases.Result result = await checkRelease(
                    typeof(Updates).Assembly.GetName().Version, activeCheck.Token);
                if (result.Download == null)
                {
                    ClearPending();
                    SetStatus("You're up to date. Latest release: " + result.Tag + ".");
                    return;
                }
                if (pending != null && pending.Version.Equals(result.Version))
                {
                    pendingNeedsCheck = false;
                    applyAfterCheck = true;
                    return;
                }
                SetStatus("Downloading release " + result.Tag + "…");
                directory = Path.Combine(dataFolder, "Updates", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string download = Path.Combine(directory, "LidAwake.exe");
                await downloadRelease(result, download, activeCheck.Token);
                if (disposed) return;
                activeCheck.Token.ThrowIfCancellationRequested();
                bool preserveManual = manual || (pending != null && pendingManual);
                SavePending(result, directory, preserveManual);
                string oldDirectory = staged == null ? null : Path.GetDirectoryName(staged);
                pending = result;
                staged = download;
                pendingManual = preserveManual;
                pendingNeedsCheck = false;
                directory = null;
                if (oldDirectory != null) DeleteDirectory(oldDirectory);
                applyAfterCheck = true;
            }
            catch (OperationCanceledException)
            {
                if (!disposed) SetStatus(canceledBySettings ? "Automatic update canceled." : "The update check timed out. Try again.");
            }
            catch (HttpRequestException error)
            {
                if (!disposed) SetStatus("Couldn't download the update. " + error.Message);
            }
            catch (SerializationException)
            {
                if (!disposed) SetStatus("GitHub returned a release that couldn't be read.");
            }
            catch (Exception error)
            {
                if (!disposed) SetStatus("Update failed: " + error.Message);
            }
            finally
            {
                if (directory != null) DeleteDirectory(directory);
                activeCheck.Dispose();
                activeCheck = null;
                Busy = false;
                Notify();
                if (applyAfterCheck && !disposed) TryApply();
            }
        }

        internal bool TryApply()
        {
            if (disposed || pending == null) return false;
            if (Busy) return false;
            if (pendingNeedsCheck)
            {
                if (canApply()) Check(pendingManual);
                return false;
            }
            if (!canApply())
            {
                SetStatus("Release " + pending.Tag + " is downloaded. It will install when the lid override is off and the tray menu is closed.");
                return false;
            }
            try
            {
                GitHubReleases.ValidateDownloadedFile(pending, staged);
                string helper = Installer.CopyHelper();
                try
                {
                    string arguments = "--apply-update " + Process.GetCurrentProcess().Id + " " +
                        Installer.Quote(Application.ExecutablePath) + " " + Installer.Quote(staged) + " " +
                        Installer.Quote(pending.Version.ToString(4)) + " " +
                        (IsLegacy(pending) ? "legacy" : "current");
                    Process.Start(new ProcessStartInfo(helper, arguments) { UseShellExecute = false, CreateNoWindow = true });
                }
                catch { File.Delete(helper); throw; }
                SetStatus("Installing release " + pending.Tag + " and restarting…");
                pending = null;
                staged = null;
                exitForUpdate();
                return true;
            }
            catch (Exception error)
            {
                SetStatus("Couldn't start the update: " + error.Message);
                return false;
            }
        }

        private void SetStatus(string value) { Status = value; Notify(); }
        private void Notify() { if (!disposed && Changed != null) Changed(this, EventArgs.Empty); }

        private void ClearPending()
        {
            string directory = staged == null ? null : Path.GetDirectoryName(staged);
            pending = null;
            staged = null;
            pendingNeedsCheck = false;
            try { File.Delete(PendingFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (directory != null) DeleteDirectory(directory);
        }

        private string PendingFile
        {
            get
            {
                string path = Path.GetFullPath(Application.ExecutablePath).ToUpperInvariant();
                using (SHA256 sha = SHA256.Create())
                {
                    string id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "");
                    return Path.Combine(dataFolder, "Updates", "pending-" + id + ".txt");
                }
            }
        }

        private void SavePending(GitHubReleases.Result result, string directory, bool manual)
        {
            if (result.Tag.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
                throw new InvalidDataException("The release tag cannot be saved.");
            if (!IsLegacy(result) && !IsCurrent(result)) throw new InvalidDataException("The release executable name cannot be saved.");
            string temporary = PendingFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, new string[] {
                    Path.GetFileName(directory), result.Tag, result.Version.ToString(4),
                    result.Download.Size.ToString(CultureInfo.InvariantCulture), result.Download.Digest ?? "",
                    Path.GetFullPath(Application.ExecutablePath), typeof(Updates).Assembly.GetName().Version.ToString(4),
                    manual ? "manual" : "automatic", result.Download.Name
                });
                if (File.Exists(PendingFile)) File.Replace(temporary, PendingFile, null);
                else File.Move(temporary, PendingFile);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static bool IsLegacy(GitHubReleases.Result result)
        {
            return string.Equals(result.Download.Name, "LidAwake-" + result.Tag + ".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCurrent(GitHubReleases.Result result)
        {
            return string.Equals(result.Download.Name, "LidAwake-" + result.Tag + "-Portable.exe", StringComparison.OrdinalIgnoreCase);
        }

        private void RestorePending(bool discard)
        {
            string directory = null;
            try
            {
                if (!File.Exists(PendingFile)) return;
                string[] fields = File.ReadAllLines(PendingFile);
                Guid id;
                Version version, sourceVersion;
                long size;
                if (fields.Length != 9 || !Guid.TryParseExact(fields[0], "N", out id) ||
                    string.IsNullOrEmpty(fields[1]) || fields[1].Length > 128 ||
                    !Version.TryParse(fields[2], out version) || !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out size) ||
                    size <= 0 || size > GitHubReleases.MaxAssetBytes ||
                    !string.Equals(fields[5], Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase) ||
                    !Version.TryParse(fields[6], out sourceVersion) || (fields[7] != "manual" && fields[7] != "automatic"))
                    throw new InvalidDataException("The saved update information is invalid.");
                directory = Path.Combine(dataFolder, "Updates", id.ToString("N"));
                if (discard || (fields[7] == "automatic" && !settings.AutoUpdatesEnabled()))
                {
                    File.Delete(PendingFile);
                    DeleteDirectory(directory);
                    return;
                }
                if (GitHubReleases.Normalize(version).Equals(GitHubReleases.Normalize(typeof(Updates).Assembly.GetName().Version)))
                {
                    // The update helper still needs this directory for its startup acknowledgement.
                    File.Delete(PendingFile);
                    return;
                }
                if (!GitHubReleases.Normalize(sourceVersion).Equals(GitHubReleases.Normalize(typeof(Updates).Assembly.GetName().Version)))
                {
                    File.Delete(PendingFile);
                    DeleteDirectory(directory);
                    return;
                }
                var asset = new GitHubReleases.Asset { Size = size, Digest = fields[4], Name = fields[8] };
                var result = new GitHubReleases.Result(fields[1], GitHubReleases.Normalize(version), asset);
                if (!IsLegacy(result) && !IsCurrent(result)) throw new InvalidDataException("The saved update name is invalid.");
                string file = Path.Combine(directory, "LidAwake.exe");
                GitHubReleases.ValidateDownloadedFile(result, file);
                pending = result;
                staged = file;
                pendingManual = fields[7] == "manual";
                pendingNeedsCheck = true;
                Status = "Release " + result.Tag + " is downloaded and will install when it is safe.";
            }
            catch (Exception)
            {
                try { File.Delete(PendingFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (directory != null) DeleteDirectory(directory);
                if (!discard) Status = "The saved update could not be read. Check for updates again.";
            }
        }

        internal static void DeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            disposed = true;
            cancellation.Cancel();
            timer.Dispose();
            cancellation.Dispose();
        }
    }

    internal static class UpdateHelper
    {
        internal static void Apply(string[] args)
        {
            Apply(args, message => MessageBox.Show(message, "Lid Awake", MessageBoxButtons.OK, MessageBoxIcon.Warning), true);
        }

        internal static void Apply(string[] args, Action<string> reportError, bool restartOnFailure)
        {
            string target = null, backup = null, temporary = null;
            bool movedOriginal = false, started = false;
            try
            {
                int pid;
                Version version;
                if (args.Length != 6 || !int.TryParse(args[1], out pid) || !Version.TryParse(args[4], out version) ||
                    (args[5] != "legacy" && args[5] != "current"))
                    throw new InvalidOperationException("Invalid update request.");
                target = Path.GetFullPath(args[2]);
                string staged = Path.GetFullPath(args[3]);
                Installer.WaitForProcess(pid);
                string suffix = ".update-" + Guid.NewGuid().ToString("N");
                temporary = target + suffix + ".new";
                backup = target + suffix + ".bak";
                File.Copy(staged, temporary);
                File.Move(target, backup);
                movedOriginal = true;
                File.Move(temporary, target);
                temporary = null;
                string ready = Path.Combine(Path.GetDirectoryName(staged), "ready");
                using (Process app = Process.Start(new ProcessStartInfo(target, "--update-ready " + Installer.Quote(ready))
                    { UseShellExecute = false }))
                {
                    started = true;
                    if (args[5] == "legacy")
                    {
                        // Old releases don't know the acknowledgement flag. Keep a backup after a health check.
                        if (app.WaitForExit(2500))
                        {
                            started = false;
                            throw new InvalidOperationException("The updated app exited immediately after starting.");
                        }
                    }
                    else
                    {
                        Stopwatch clock = Stopwatch.StartNew();
                        while (!File.Exists(ready) && !app.HasExited && clock.Elapsed < TimeSpan.FromSeconds(20))
                            Thread.Sleep(100);
                        if (!File.Exists(ready))
                        {
                            if (!app.HasExited)
                            {
                                reportError("Lid Awake restarted but did not confirm the update. The previous executable is saved at:\r\n\r\n" + backup);
                                return;
                            }
                            started = false;
                            throw new InvalidOperationException("The updated app did not start.");
                        }
                        if (app.WaitForExit(1500))
                        {
                            started = false;
                            throw new InvalidOperationException("The updated app exited immediately after starting.");
                        }
                    }
                }
                try { Installer.UpdateRegisteredVersion(target, version); } catch { }
                if (args[5] != "legacy")
                    try { File.Delete(backup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try
                {
                    File.Delete(ready);
                    File.Delete(staged);
                    Directory.Delete(Path.GetDirectoryName(staged), false);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            catch (Exception error)
            {
                if (temporary != null) try { File.Delete(temporary); } catch { }
                if (movedOriginal && !started)
                {
                    try
                    {
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(backup, target);
                        movedOriginal = false;
                    }
                    catch { /* Keep the backup and tell the user where to find it. */ }
                }
                if (restartOnFailure && !started && target != null && File.Exists(target))
                    try { Process.Start(new ProcessStartInfo(target, "--update-failed") { UseShellExecute = false }); } catch { }
                string recovery = movedOriginal ? "\r\n\r\nThe previous executable is saved at:\r\n" + backup : "";
                reportError("Lid Awake could not install the update.\r\n\r\n" + error.Message + recovery);
            }
        }
    }
}
