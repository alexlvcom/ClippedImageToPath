# Clipped Image to Path

A Windows tray app that saves every clipboard image as a PNG file and hands you
its **file path** on `Shift+Insert` — while `Ctrl+V` still pastes the real image
everywhere else.

Windows Terminal cannot paste a bitmap into a CLI tool. Coding agents want a path,
not a clipboard image. This bridges the two.

![Tray menu](ClippedImageToPath-Tray.jpg)

## Why you'd want it

Coding agents work with images by **file path**. Some can grab an image off your
clipboard when they run right there on your Windows box — but that breaks the
moment the agent is not local:

- **Agent in an SSH terminal on another server.** There is no clipboard there to
  paste into.
- **Remote Desktop, WinSCP, any remote session.** The screenshot is on your side,
  not on the machine the agent runs on.

Turn on **remote upload** and every screenshot lands on the target server over
SFTP/FTP/FTPS. Then just tell the agent *"check the screenshots at `<folder>`"*
and it can open exactly what you are looking at.

## Features

- **Watches the clipboard** and saves any image as `clipboard_yyyy-MM-dd_HH-mm-ss_fff.png`.
- **Smart paste** — the image stays on the clipboard for Teams, Gmail and other GUI apps; `Shift+Insert` supplies the quoted path instead.
- **WSL mode** — pastes `/mnt/c/...` instead of `C:\...`.
- **Remote upload** over SFTP, FTP or FTPS, with multiple named server profiles and a per-profile connection test.
- **Encrypted passwords** — server credentials are protected with Windows DPAPI, per user and machine.
- **Optional reminders** while remote upload is on, so you do not leave it running by accident.
- Leaves ordinary text clipboard entries untouched, with loop prevention, debounce, retry and dedupe hashing.

## Install

Download **[ClippedImageToPath-Setup.exe](https://github.com/alexlvcom/ClippedImageToPath/releases/latest/download/ClippedImageToPath-Setup.exe)**
and run the installer. No .NET runtime is needed. By default it installs in
`%LOCALAPPDATA%\Programs\Clipped Image to Path` without administrator approval.
You can also choose an all-users install in Program Files.
Choose **Start with Windows** during setup if you want it to start automatically.
Start the app from the Start menu; it runs in the system tray.
When upgrading, setup closes the running app and restarts the installed copy.
If you used an older standalone EXE, exit that copy before your first installer upgrade.
Uninstall it through Windows Settings → Apps. The uninstaller exits the app automatically.

The installed executable is
`%LOCALAPPDATA%\Programs\Clipped Image to Path\ClippedImageToPath.exe`.
Open **About** from the tray menu to see and copy the full path of the running EXE.
Settings stay in `%APPDATA%\ClippedImageToPath` during upgrades and uninstall.

The exe is unsigned, so Windows may show a SmartScreen warning on first run. See
[Install and first run](docs/GUIDE.md#install-and-first-run) for what that means
and how to verify the download.

## Settings

Open **Settings** from the tray menu. The compact window has four tabs:
**Saved images**, **Clipboard**, **Remote upload**, and **Updates**.
**Open output folder** is beside Browse in Saved images.
The Settings title shows your current version. Save applies changes across all tabs;
Cancel leaves them unchanged.

## Updates

The app checks GitHub when it starts and once a day by default. In Settings,
choose once an hour, day, week, month, or **Never**. Never also disables the
startup check. Manual checks remain available.
When a newer installer release is available, choose **Update** to download it,
verify its SHA-256, and open setup. Setup closes the app and restarts it after
upgrading. **Later** dismisses that version for the current session.
Use **Check for updates** in Settings → **Updates** to check or reopen the offer at any time.

## Documentation

- **[User guide](docs/GUIDE.md)** — installing, the tray menu, every setting, remote upload, and troubleshooting.
- **[Development](docs/DEVELOPMENT.md)** — building from source and the versioning conventions.
- **[Changelog](CHANGELOG.md)** — what changed in each version.
