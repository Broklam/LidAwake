using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LidAwake;

internal static class ReleaseTests
{
    private static int assertions;
    private static string root;
    private static string fixture;
    private static string broken;
    private static string original;
    private static string legacy;

    [STAThread]
    private static void Main(string[] args)
    {
        root = args[0];
        fixture = args[1];
        broken = args[2];
        original = args[3];
        legacy = args[4];
        try
        {
            TestReleaseSelection();
            TestDownload();
            TestChooserAndSettings();
            TestIsolatedInstall();
            TestPendingUpdate();
            TestCoordinator();
            TestReplacement();
            Console.WriteLine("PASS: " + assertions + " assertions, including isolated install, download, deferred update, replacement and rollback.");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Environment.ExitCode = 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception("Assertion failed: " + message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        bool failed = false;
        try { action(); } catch (Exception) { failed = true; }
        Assert(failed, message);
    }

    private static string Hash(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (Stream input = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }

    private static string Asset(string tag, string name, long size, string digest)
    {
        return "{\"name\":\"" + name + "\",\"browser_download_url\":\"https://github.com/Broklam/LidAwake/releases/download/" +
            tag + "/" + name + "\",\"size\":" + size + ",\"digest\":" +
            (digest == null ? "null" : "\"" + digest + "\"") + "}";
    }

    private static GitHubReleases.Result Parse(string json, string installed)
    {
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            return GitHubReleases.ReadRelease(stream, new Version(installed));
    }

    private static string Json(string tag, params string[] assets)
    {
        return "{\"tag_name\":\"" + tag + "\",\"assets\":[" + string.Join(",", assets) + "]}";
    }

    private static void TestReleaseSelection()
    {
        string portable = Asset("v1.2.3", "LidAwake-v1.2.3-Portable.exe", 123, null);
        string legacy = Asset("v1.2.3", "LidAwake-v1.2.3.exe", 123, null);
        Assert(Parse(Json("v1.2.3"), "1.2.3.0").Download == null, "three- and four-part versions match");
        Assert(Parse(Json("v1.2.3", legacy, portable), "1.2.2.0").Download.Name.EndsWith("-Portable.exe"), "prefer portable asset");
        Assert(Parse(Json("v1.2.3", legacy), "1.2.4.0").Download == null, "automatic updates skip older releases");
        Assert(Parse(Json("v1.2.3", legacy), "1.2.2.0").Download.Name == "LidAwake-v1.2.3.exe", "newer legacy asset is supported");
        ExpectFailure(() => Parse(Json("v1.2.3", Asset("v1.2.3", "LidAwake-v1.2.3-Setup.exe", 123, null)), "1.2.2.0"), "reject setup-only release");
        ExpectFailure(() => Parse(Json("v1.2.3", portable.Replace("github.com", "example.com")), "1.2.2.0"), "reject other asset host");
        ExpectFailure(() => Parse(Json("v1.2.3", portable.Replace("/Broklam/LidAwake/", "/attacker/LidAwake/")), "1.2.2.0"), "reject other repository");
        ExpectFailure(() => Parse(Json("v1.2.3", Asset("v1.2.3", "LidAwake-v1.2.3.exe", 0, null)), "1.2.2.0"), "reject empty asset");
        ExpectFailure(() => Parse(Json("v1.2.3", Asset("v1.2.3", "LidAwake-v1.2.3.exe", 123, "sha256:wrong")), "1.2.2.0"), "reject malformed digest");
        ExpectFailure(() => Parse(Json("not-a-version"), "1.2.2.0"), "reject incomparable tags");
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly byte[] payload;
        private readonly string json;
        internal int Calls;
        internal FixtureHandler(string json, byte[] payload) { this.json = json; this.payload = payload; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            bool metadata = request.RequestUri.Host == "api.github.com";
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = metadata ? (HttpContent)new StringContent(json, Encoding.UTF8, "application/json") : new ByteArrayContent(payload);
            return Task.FromResult(response);
        }
    }

    private static void TestDownload()
    {
        byte[] bytes = File.ReadAllBytes(fixture);
        string asset = Asset("v1.2.3", "LidAwake-v1.2.3-Portable.exe", bytes.Length, "sha256:" + Hash(fixture));
        var handler = new FixtureHandler(Json("v1.2.3", asset), bytes);
        using (var client = new HttpClient(handler))
        {
            GitHubReleases.Result result = GitHubReleases.CheckAsync(client, new Version(1, 2, 2, 0), CancellationToken.None).GetAwaiter().GetResult();
            string path = Path.Combine(root, "download.exe");
            GitHubReleases.DownloadAsync(client, result, path, CancellationToken.None).GetAwaiter().GetResult();
            Assert(handler.Calls == 2 && Hash(path) == Hash(fixture), "metadata and verified download use the fixtures");
            byte[] tampered = (byte[])bytes.Clone();
            tampered[tampered.Length - 1] ^= 1;
            var badHandler = new FixtureHandler(Json("v1.2.3", asset), tampered);
            using (var badClient = new HttpClient(badHandler))
            {
                string badPath = Path.Combine(root, "bad-download.exe");
                ExpectFailure(() => GitHubReleases.DownloadAsync(badClient, result, badPath, CancellationToken.None).GetAwaiter().GetResult(), "reject altered checksum");
                Assert(!File.Exists(badPath), "remove invalid downloads");
            }
            var shortHandler = new FixtureHandler(Json("v1.2.3", asset), new byte[] { 1, 2, 3 });
            using (var shortClient = new HttpClient(shortHandler))
                ExpectFailure(() => GitHubReleases.DownloadAsync(shortClient, result, Path.Combine(root, "short.exe"), CancellationToken.None).GetAwaiter().GetResult(), "reject incomplete download");
            var wrongVersion = new GitHubReleases.Result("v1.2.4", new Version(1, 2, 4, 0), result.Download);
            ExpectFailure(() => GitHubReleases.ValidateDownloadedFile(wrongVersion, path), "reject binary with wrong version");
        }
    }

    private static void TestChooserAndSettings()
    {
        string portable = Path.Combine(root, "portable.exe");
        string[] none = new string[0];
        Assert(Installer.IsSetup("LidAwake-v1.1.1-Setup.exe"), "setup asset launches installer");
        Assert(Installer.IsSetup("LidAwake-v1.1.1-Setup (1).exe"), "Windows duplicate setup name launches installer");
        Assert(Installer.IsSetup("LidAwake-v1.1.1-setup (12).exe"), "setup detection ignores case and accepts multi-digit duplicates");
        Assert(!Installer.IsSetup("LidAwake-v1.1.1-Portable (1).exe"), "duplicate portable remains standalone");
        string installed = Path.Combine(root, "custom install", "LidAwake.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(installed));
        File.Copy(fixture, installed);
        Assert(LaunchChoice.Resolve(portable, none, "", null) == LaunchAction.Ask, "first portable launch asks");
        Assert(LaunchChoice.Resolve(portable, none, "standalone", null) == LaunchAction.Standalone, "remember standalone");
        Assert(LaunchChoice.Resolve(portable, none, "installed", installed) == LaunchAction.Installed, "remember custom installation");
        Assert(LaunchChoice.Resolve(portable, none, "installed", null) == LaunchAction.Ask, "missing install returns to chooser");
        Assert(LaunchChoice.Resolve(installed, none, "", installed) == LaunchAction.Standalone, "custom installation bypasses chooser");
        Assert(LaunchChoice.Resolve(portable, new string[] { "--startup" }, "", null) == LaunchAction.Standalone, "startup bypasses chooser");
        Assert(LaunchChoice.Resolve(portable, new string[] { "--update-ready", "file" }, "", null) == LaunchAction.Standalone, "update restart bypasses chooser");
        string folder = Path.Combine(root, "settings");
        var settings = new AppSettings(folder, portable);
        Assert(settings.AutoUpdatesEnabled(), "automatic updates are enabled by default");
        settings.SetAutoUpdates(false);
        Assert(!settings.AutoUpdatesEnabled(), "an explicit automatic update opt-out persists");
        settings.SetAutoUpdates(true);
        Assert(settings.AutoUpdatesEnabled(), "automatic update setting persists");
        settings.SaveLaunchChoice("standalone");
        Assert(settings.LoadLaunchChoice() == "standalone", "standalone choice persists");
        settings.ClearLaunchChoice();
        Assert(settings.LoadLaunchChoice() == "", "launch choice can be reset");
    }

    private static void TestIsolatedInstall()
    {
        string folder = Path.Combine(root, "isolated-install");
        string app = Path.Combine(folder, "LidAwake.exe");
        string uninstaller = Path.Combine(folder, "Uninstall.exe");
        string shortcut = Path.Combine(folder, "Lid Awake.lnk");
        Installer.InstallFiles(fixture, app, uninstaller, false);
        Assert(Hash(app) == Hash(fixture) && Hash(uninstaller) == Hash(fixture), "install creates separate app and uninstaller");
        Assert(!Installer.CanInstallOver(app, new Version(1, 2, 2, 0)), "setup refuses to replace a newer installed copy");
        Assert(Installer.CanInstallOver(app, new Version(1, 2, 3, 0)), "setup accepts the same version");
        Installer.CreateShortcut(app, shortcut);
        Assert(File.Exists(shortcut), "install creates a Start menu shortcut");
        var values = Installer.RegistrationValues(app, uninstaller, new Version(1, 2, 3, 0));
        Assert((string)values["DisplayVersion"] == "1.2.3", "Apps version registration value");
        Assert((string)values["InstallLocation"] == folder, "Apps records the selected folder");
        Assert((string)values["UninstallString"] == "\"" + uninstaller + "\" --uninstall", "Apps points to independent uninstaller");
        File.Copy(original, app, true);
        File.Copy(original, uninstaller, true);
        ExpectFailure(() => Installer.InstallFiles(fixture, app, uninstaller, false), "fresh setup cannot overwrite existing files");
        Assert(Hash(app) == Hash(original) && Hash(uninstaller) == Hash(original), "fresh setup leaves existing files intact");
        using (FileStream locked = new FileStream(uninstaller, FileMode.Open, FileAccess.Read, FileShare.None))
            ExpectFailure(() => Installer.InstallFiles(fixture, app, uninstaller), "setup stops when an existing uninstaller is locked");
        Assert(Hash(app) == Hash(original) && Hash(uninstaller) == Hash(original), "setup rolls back a partially replaced installation");
    }

    private static void TestPendingUpdate()
    {
        string folder = Path.Combine(root, "pending-data");
        string directory = Path.Combine(folder, "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string staged = Path.Combine(directory, "LidAwake.exe");
        File.Copy(fixture, staged);
        var asset = new GitHubReleases.Asset { Name = "LidAwake-v1.2.3-Portable.exe", Size = new FileInfo(staged).Length, Digest = "sha256:" + Hash(staged) };
        var result = new GitHubReleases.Result("v1.2.3", new Version(1, 2, 3, 0), asset);
        var settings = new AppSettings(folder, Application.ExecutablePath);
        var first = new Updates(settings, folder, () => false, () => { throw new Exception("unexpected exit"); }, false);
        MethodInfo save = typeof(Updates).GetMethod("SavePending", BindingFlags.NonPublic | BindingFlags.Instance);
        save.Invoke(first, new object[] { result, directory, true });
        first.Dispose();
        var restored = new Updates(settings, folder, () => false, () => { throw new Exception("unexpected exit"); }, false);
        Assert(!restored.TryApply() && restored.Status.Contains("downloaded"), "pending update is retained and waits for lid safety");
        restored.SettingsSaved(true, false);
        Assert(File.Exists(staged), "disabling automatic updates retains a manually requested update");
        restored.Dispose();

        var automatic = new Updates(settings, folder, () => false, () => { throw new Exception("unexpected exit"); }, false);
        save.Invoke(automatic, new object[] { result, directory, false });
        automatic.Dispose();
        settings.SetAutoUpdates(true);
        var afterRestart = new Updates(settings, folder, () => false, () => { throw new Exception("unexpected exit"); }, false);
        settings.SetAutoUpdates(false);
        afterRestart.SettingsSaved(true, false);
        Assert(!File.Exists(staged), "turning off automatic updates removes an automatic pending update");
        afterRestart.Dispose();
    }

    private static void TestCoordinator()
    {
        string folder = Path.Combine(root, "coordinator");
        var settings = new AppSettings(folder, Application.ExecutablePath);
        var asset = new GitHubReleases.Asset { Name = "LidAwake-v1.2.3-Portable.exe",
            Size = new FileInfo(fixture).Length, Digest = "sha256:" + Hash(fixture) };
        var result = new GitHubReleases.Result("v1.2.3", new Version(1, 2, 3, 0), asset);
        GitHubReleases.Result latest = result;
        int checks = 0, downloads = 0;
        Func<Version, CancellationToken, Task<GitHubReleases.Result>> check = (version, token) =>
        {
            checks++;
            return Task.FromResult(latest);
        };
        Func<GitHubReleases.Result, string, CancellationToken, Task> download = (release, path, token) =>
        {
            downloads++;
            File.Copy(fixture, path);
            return Task.FromResult(0);
        };
        var updates = new Updates(settings, folder, () => false, () => { throw new Exception("unexpected exit"); }, false, check, download);
        updates.SettingsSaved(false, true);
        Assert(checks == 1 && downloads == 1 && updates.Status.Contains("ready"), "enabling automatic updates downloads and queues a newer release");
        updates.Check();
        Assert(checks == 2 && downloads == 1, "a pending update still checks latest without re-downloading");
        updates.Dispose();
        var reloaded = new Updates(settings, folder, () => false, () => { throw new Exception("unexpected exit"); }, false, check, download);
        reloaded.Check();
        Assert(checks == 3 && downloads == 1 && reloaded.Status.Contains("ready"), "resumed pending update is rechecked");
        latest = new GitHubReleases.Result("v1.1.0", new Version(1, 1, 0, 0), null);
        reloaded.Check();
        Assert(reloaded.Status.Contains("up to date") &&
            Directory.GetFiles(Path.Combine(folder, "Updates"), "pending-*.txt").Length == 0,
            "a newer check clears a stale queued release when the installed version is latest");
        reloaded.Dispose();
    }

    private static void TestReplacement()
    {
        string appDir = Path.Combine(root, "replacement");
        string stageDir = Path.Combine(root, "replacement-stage");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(stageDir);
        string target = Path.Combine(appDir, "LidAwake.exe");
        string stage = Path.Combine(stageDir, "LidAwake.exe");
        File.Copy(original, target);
        File.Copy(fixture, stage);
        int exited;
        using (Process process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true }))
        {
            exited = process.Id;
            process.WaitForExit();
        }
        string error = null;
        UpdateHelper.Apply(new string[] { "--apply-update", exited.ToString(), target, stage, "1.2.3.0", "current" }, message => error = message, false);
        Assert(error == null && Hash(target) == Hash(fixture), "helper replaces and verifies a healthy inert app");
        Assert(Directory.GetFiles(appDir, "*.bak").Length == 0, "backup removed after healthy startup");
        Thread.Sleep(1700);

        File.Copy(original, target, true);
        Directory.CreateDirectory(stageDir);
        File.Copy(broken, stage);
        error = null;
        UpdateHelper.Apply(new string[] { "--apply-update", exited.ToString(), target, stage, "1.2.3.0", "current" }, message => error = message, false);
        Assert(error != null && Hash(target) == Hash(original), "helper rolls back when updated app exits before readiness");
        Assert(Directory.GetFiles(appDir, "*.bak").Length == 0, "rollback restores original filename");

        File.Delete(stage);
        File.Copy(legacy, stage);
        error = null;
        UpdateHelper.Apply(new string[] { "--apply-update", exited.ToString(), target, stage, "1.2.3.0", "legacy" }, message => error = message, false);
        string[] backups = Directory.GetFiles(appDir, "*.bak");
        Assert(error == null && Hash(target) == Hash(legacy), "legacy app without acknowledgement remains healthy");
        Assert(backups.Length == 1 && Hash(backups[0]) == Hash(original), "legacy update retains recovery backup");
        Thread.Sleep(2200);
    }
}
