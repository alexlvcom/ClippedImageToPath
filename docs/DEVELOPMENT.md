# Clipped Image to Path — development

Requires the .NET 8 SDK. Nothing else.

## Build

```powershell
dotnet build -c Release
```

## Publish

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Output: `publish\ClippedImageToPath.exe` — the same single-file, self-contained
Windows x64 executable the releases ship.

## Project layout

```
Program.cs                  app logic, tray UI, settings, clipboard listener
ClippedImageToPath.csproj   project metadata and version
CHANGELOG.md                user-visible change history
```

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

The spaced name **Clipped Image to Path** is what the user reads: the About window,
the Settings title, the tray tooltip, every dialog caption, `<Product>`, the README.
It lives in the single `AppName` constant in `Program.cs`.

The one-word `ClippedImageToPath` is reserved for identifiers — the assembly, the
executable, the single-instance mutex, the listener window caption, and the
`%APPDATA%\ClippedImageToPath` settings folder, which must not change or existing
settings would be orphaned.
