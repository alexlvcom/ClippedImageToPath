# Clipped Image to Path — user guide

- [Install and first run](#install-and-first-run)
- [What it does](#what-it-does)
- [Typical workflow](#typical-workflow)
- [The tray menu](#the-tray-menu)
- [Settings](#settings)
- [Remote upload](#remote-upload)
- [Where your data lives](#where-your-data-lives)
- [Notes](#notes)
- [Troubleshooting](#troubleshooting)

## Install and first run

Download **[ClippedImageToPath.exe](https://github.com/alexlvcom/ClippedImageToPath/releases/latest/download/ClippedImageToPath.exe)**
from the [releases page](https://github.com/alexlvcom/ClippedImageToPath/releases/latest).
It is a single self-contained executable — no .NET runtime install needed. Just
download and double-click; it starts in the system tray.

### Windows SmartScreen

The exe is **unsigned** (there is no paid code-signing certificate for a free hobby
tool), so Windows may show **"Windows protected your PC."** This is *not* a virus
warning — it only means the file is new and has not built up download reputation
yet. Click **More info → Run anyway**.

Prefer to be sure? The source is right here — see [Development](DEVELOPMENT.md)
and build it yourself.

### Verifying the download

Each release lists the SHA-256 of the exe:

```powershell
Get-FileHash .\ClippedImageToPath.exe -Algorithm SHA256
```

Compare it against the hash on that version's
[release page](https://github.com/alexlvcom/ClippedImageToPath/releases/latest).

## What it does

- Watches clipboard updates in the background.
- Detects image content — screenshots, snips, browser images and so on.
- Saves the image as a PNG with a timestamped name:
  `clipboard_yyyy-MM-dd_HH-mm-ss_fff.png`.
- Keeps the image on the clipboard, so Teams, Gmail and other GUI apps still get a
  normal `Ctrl+V` paste.
- On `Shift+Insert`, temporarily supplies the quoted path text instead:
  - Windows mode: `"C:\...\clipboard_....png"`
  - WSL mode: `"/mnt/c/.../clipboard_....png"`
- Optionally uploads the PNG to a remote server after saving, over SFTP (SSH), FTP
  or FTPS (explicit or implicit TLS).
- Optionally reminds you with a Windows notification while remote upload is
  enabled, at a configurable interval (30 minutes by default).
- Leaves normal text clipboard entries unchanged.
- Includes loop prevention, debounce, retry logic and dedupe hashing.

## Typical workflow

1. Copy an image — Snipping Tool, a browser, any screenshot tool.
2. The app saves the PNG.
3. Use `Ctrl+V` to paste the real image into GUI apps, or `Shift+Insert` to paste
   the quoted path into a terminal.

## The tray menu

| Item | Behaviour |
|---|---|
| **Enable Remote Upload** | Checkable; turns remote upload on or off. The menu stays open so you can see the checkbox change |
| **Active server** | Which server profile uploads use. Disabled while remote upload is off |
| **Open output folder** | |
| **Settings** | |
| **About** | Name, version, output folder, build date, copyright |
| **Exit** | |

## Settings

![Settings dialog](../ClippedImageToPath-Config.png)

Hover over any option to see a short description of what it does.

| Setting | What it controls |
|---|---|
| **Output folder** | Where saved PNG files go |
| **Convert clipboard path to WSL format** | Pastes `/mnt/c/...` instead of `C:\...` |
| **Paste path with `Shift+Insert`** | Turns the path paste on or off |
| **Remote upload enabled** | Uploads each saved PNG to the active server |
| **Remote upload notifications** | Reminder toggle plus the interval in minutes (30 by default) |
| **Remote servers…** | Add, edit, remove and pick the active server profile |

## Remote upload

Each server profile stores its protocol (SFTP / FTP / FTPS explicit / FTPS
implicit), host, port, user, password, remote directory and passive mode. A
per-profile **test connection** button verifies both login and remote directory
access before you rely on it.

Switch the active server quickly from the tray menu's **Active server** submenu;
uploads always use the active profile.

Server passwords are **encrypted with Windows DPAPI (per-user scope)** — they can
only be decrypted by the same Windows user account on the same machine, and are
never stored as plain text. Configs from older versions that stored passwords as
plain base64 are upgraded automatically on first launch.

The reminder notifications exist because remote upload is easy to leave on by
accident; they nudge you to disable it once you no longer need it.

## Where your data lives

| Path | Contents |
|---|---|
| `%APPDATA%\ClippedImageToPath\settings.json` | All settings and server profiles |
| `<OutputFolder>\bridge.log` | Runtime log |

## Notes

- Works standalone; no clipboard manager is required.
- Clipboard managers such as Ditto are optional and can be used alongside it.
- If the output path has spaces, the quoted clipboard text prevents CLI parsing
  issues.
- In a clipboard manager you may see two entries for one screenshot: the original
  image clip from your screenshot tool, and the temporary path clip written for a
  terminal paste. To hide the second, add `ClippedImageToPath.exe` to the
  manager's ignore-app list.
- `Shift+Insert` always requests the saved path, so it also works in terminals
  hosted inside editors and other applications.

## Troubleshooting

| Symptom | What to check |
|---|---|
| Nothing happens | Confirm the app is running in the tray, then check `<OutputFolder>\bridge.log` |
| Clipboard seems busy | The app retries clipboard access automatically |
| Path format is unexpected | Check the WSL conversion toggle in **Settings** |
| Uploads fail | Use the per-profile **test connection** button; the log records the error |
