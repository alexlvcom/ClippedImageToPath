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

Download **[ClippedImageToPath.exe](https://github.com/alexlvcom/ClippedImageToPath/releases/latest/download/ClippedImageToPath.exe)**
and double-click it. It is a single self-contained executable — no .NET runtime
needed, no installer. It starts in the system tray.

The exe is unsigned, so Windows may show a SmartScreen warning on first run. See
[Install and first run](docs/GUIDE.md#install-and-first-run) for what that means
and how to verify the download.

## Documentation

- **[User guide](docs/GUIDE.md)** — installing, the tray menu, every setting, remote upload, and troubleshooting.
- **[Development](docs/DEVELOPMENT.md)** — building from source and the versioning conventions.
- **[Changelog](CHANGELOG.md)** — what changed in each version.
