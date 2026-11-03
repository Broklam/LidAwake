# Lid Awake

Lid Awake is a portable tray app for Windows 10 and 11. Download the latest EXE from the [Releases page](https://github.com/Broklam/LidAwake/releases/latest). If an older copy is running, choose **Exit** from its tray menu before opening the new EXE. Then click the laptop icon by the clock. Look under the **^** arrow if Windows puts it among the hidden icons.

- Click **Keep awake with lid closed** to turn it on. The icon turns green with a checkmark.
- Choose **Keep awake for 1 hour** when off, or **Restore normal in 1 hour** when already on. The icon tooltip and tray menu show the remaining time. You can restart the timer or switch back to staying awake until you turn it off.
- Choose **Restore normal now** or **Exit** to restore your original lid settings immediately.

It works on battery and when plugged in. Windows can still sleep after its usual idle timeout or when you choose Sleep. If the app is forcibly stopped while on, open it again to restore the settings it saved. The app opens with the override off.

Open **Settings** from the tray menu to start Lid Awake automatically when you sign in. It starts with the lid override off. If you move the EXE, open Settings from its new location and save to update the startup entry.

In **Settings**, click **Check for updates** to compare the latest published release with the installed version. The official repository is filled in by default; you can change it to another public GitHub repository as `owner/repo` or a GitHub URL and click **Save**. When an update is available, open its release page and download the new EXE; Lid Awake does not install updates automatically.

No installer or extra runtime download is needed.

To rebuild from this folder, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1`. The icons are embedded in the EXE.

Every push to the `main` branch runs the GitHub Actions release workflow. It builds a versioned EXE, creates a numbered GitHub release, and attaches the EXE. The major and minor version come from `Program.cs`; the patch number is the workflow run number. You can also run the workflow manually from the Actions tab.
