# Lid Awake

## Overview

Lid Awake is a tray app for Windows 10 and 11 that keeps your laptop awake when you close its lid. Use it on battery or while plugged in, for one hour or until you turn it off.

![Lid Awake tray menu showing the override on and the options to restore normal lid behavior](assets/tray-example.png)

## Quickstart

Open the release marked **Latest** and look under **Assets**. [View releases on GitHub](https://github.com/Broklam/LidAwake/releases)

If the release offers only `LidAwake-vX.Y.Z.exe`, open it to run standalone. If it offers both packages below, choose the one that fits.

- **Setup (`LidAwake-vX.Y.Z-Setup.exe`).** Open the file and click **Install**. Setup installs for your Windows account without requiring administrator privileges and adds a Start menu shortcut and an entry in Windows Settings → Apps. The default folder is usually `%LOCALAPPDATA%\Programs\Lid Awake`. To pick a different folder, type its path or click **Browse** and choose a new or empty folder. Setup reuses the folder of an existing installation. To move an installation, uninstall it first and run Setup again.
- **Portable (`LidAwake-vX.Y.Z-Portable.exe`).** Open the file, select **Run standalone (no installation)**, and click **Continue**. You can also select **Install for this Windows account**.

Lid Awake uses the runtime included with Windows and starts with the override off. Find the laptop icon by the clock or under the tray's **^** arrow. Click it and select **Keep awake with lid closed**. The icon turns green with a checkmark.

## Use the tray menu

- **Set a one-hour timer.** Choose **Keep awake for 1 hour** when the override is off or **Restore normal in 1 hour** when it's on. The tray menu and icon tooltip show the time remaining. While the timer is active, choose **Restart 1-hour timer** or **Keep awake until turned off**.

  ![Lid Awake tray menu showing 60 minutes left and options to restart the timer or keep the override on](assets/tray-timer-example.png)

- **Restore your lid settings.** Choose **Restore normal now** to turn the override off or **Exit** to restore your settings and close the app. If Lid Awake is forcibly stopped while the override is on, reopen it to recover the saved settings.

The override changes what Windows does when you close the lid. Windows can still sleep after its usual idle timeout or when you choose **Sleep**.

## Settings and updates

Sign-in startup is available in older standalone releases too. The launch choice and automatic updates described here are available in builds distributed as Setup and Portable.

- **Start at sign-in.** Open **Settings...**, select **Start Lid Awake when I sign in to Windows**, and click **Save**. If you move a standalone EXE, open Settings from its new location and save to update the startup entry.
- **Remember a launch choice.** Select **Don't ask again** in the portable launch dialog to remember how to open the app. If you remember the install choice, the portable file opens the installed copy as long as it's present. Choose **Show install options next time** in Settings to reset the choice. Installed copies and sign-in launches bypass the dialog.
- **Automatic updates.** Lid Awake checks GitHub releases shortly after startup and every six hours while running. It downloads and verifies new versions, then installs the update and restarts once the tray menu and Settings are closed and the original lid behavior has been restored. Updates keep the existing executable path and preferences. If the new version fails to start, Lid Awake attempts to restore the previous executable. To opt out, clear **Automatically check for and install updates** and click **Save**.

## Uninstall

- **Installed copy.** Choose **Exit** from the tray menu, then uninstall Lid Awake from **Windows Settings → Apps**. Your saved preferences are kept.
- **Standalone copy.** If you enabled sign-in startup for this copy, clear that setting and click **Save**. Choose **Exit** from the tray menu, then delete the EXE.

## Build and test

Run these commands from the repository root in PowerShell on Windows. The build uses the Windows .NET Framework 4 compiler and embeds the app icons.

**Build a single executable.** This creates `LidAwake.exe` in the repository root.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

**Build release packages.** This creates the Setup and Portable executables in `dist`. To set the version, append `-Version 1.2.3.0`. Package versions have four numeric components and a final component of zero.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1
```

**Run the isolated tests.** The suite uses local fixtures to exercise release selection and downloads, launch choices, installation and shortcuts, and update deferral, replacement, and rollback.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Test.ps1
```

GitHub Actions runs **Windows tests** on pushes to `main` and pull requests. **Build and release** runs on pushes to `main`, tests the app, and publishes both packages. It takes the major and minor version from `Program.cs` and the patch number from the workflow run number. Both workflows can also be started from the Actions tab.
