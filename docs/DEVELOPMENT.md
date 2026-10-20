# Clipped Image to Path — development

Requires Windows, the .NET 8 SDK, and Inno Setup 6.7.3 to build the installer.

## Build

```powershell
dotnet build -c Release
```

## Build the installer

```powershell
./scripts/build-installer.ps1
```

Output: `dist\ClippedImageToPath-Setup.exe` and `dist\SHA256SUMS.txt`.
The script reads the version from the project file and publishes a self-contained
Windows x64 app before compiling the Inno Setup installer. Pass `-IsccPath` if
Inno Setup is installed outside its default location.

Push a matching version tag (for example `v1.7.0`) to build and publish a GitHub
release with only the installer and checksum as download assets. Manual workflow
runs build a downloadable artifact without publishing a release.

## Publish the app without packaging

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Output: `publish\ClippedImageToPath.exe` — the same single-file, self-contained
Windows x64 executable used inside the installer.

## Project layout

```
Program.cs                  app logic, tray UI, settings, clipboard listener
ClippedImageToPath.csproj   project metadata and version
ClippedImageToPath.ico      multi-resolution application icon
CHANGELOG.md                user-visible change history
```

## Update checks

`UpdateService.cs` reads the latest stable GitHub release and verifies installer
downloads using SHA-256. The tray context checks at startup and daily by default. `UpdateSchedule.cs`
handles the saved hourly/daily/weekly/calendar-monthly/never setting.

```powershell
dotnet run --project tests/Updater.Tests.csproj -c Release
```

Tests use a fake HTTP handler and never launch an installer.

## Dependencies

| Package | Used for |
|---|---|
| `SSH.NET` | SFTP uploads |
| `FluentFTP` | FTP and FTPS uploads |
| `System.Security.Cryptography.ProtectedData` | DPAPI encryption of stored passwords |

## Versioning

Four values in `ClippedImageToPath.csproj` must stay in sync, and must match the
newest heading in `CHANGELOG.md`:

- `Version` and `InformationalVersion` use `N.N.N`
- `FileVersion` and `AssemblyVersion` use `N.N.N.0`

Add the changelog entry and bump all four in the same change.

## Naming

The spaced name **Clipped Image to Path** is what the user reads: the About tab,
the Settings title, the tray tooltip, every dialog caption, `<Product>`, the README.
It lives in `AppInfo.DisplayName` (`AppInfo.cs`).

## UI

The tray icon, clipboard listener, paste hook and message loop are WinForms (`Program.cs`).
Every window is WPF in `Ui/` (Settings, Remote Servers, the server editor, the update
dialog), styled by the shared AlexLV Windows UI kit vendored unchanged in
`AlexLV.WindowsUi/`; its canonical copy is `C:projectsAlexLV.WindowsUi`. Windows derive
from `ThemedWindow`, messages use `MessageDialog`, and the tray menu is drawn by
`TrayMenuStyler`. The approved design is `design/ui-redesign.html`.
The app version comes from the `.csproj` and release notes from the embedded `CHANGELOG.md`
(`Changelog.cs`). Keep `InvariantGlobalization` off: WPF text layout needs culture data.

`ClippedImageToPath.exe --render-windows <folder>` builds every window off screen in the light and dark
themes, saves a PNG per Settings tab and dialog, and writes `render.txt` with any tab whose content would
need vertical scrolling. It runs beside the tray instance and never saves settings.

The one-word `ClippedImageToPath` is reserved for identifiers — the assembly, the
executable, the single-instance mutex, the listener window caption, and the
`%APPDATA%\ClippedImageToPath` settings folder, which must not change or existing
settings would be orphaned.
