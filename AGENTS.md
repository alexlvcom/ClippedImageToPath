# Repository Instructions

- When starting user-visible changes after a release, set the application version to the next intended release version.
- Do not bump the version again for additional changes or testing iterations before that release. Application versions must correspond to published releases, not individual development changes.
- Keep all changes planned for the same unreleased version under one changelog heading.
- After implementing a user-visible app change, publish and deploy it to the local installation, then restart the running app so the user can test the result.
- Keep version values in `ClippedImageToPath.csproj` in sync: `Version` and `InformationalVersion` use `N.N.N`; `FileVersion` and `AssemblyVersion` use `N.N.N.0`.
- Do not include commit hashes, source revision IDs, or other unique build identifiers in the About window version.

## Installer and local deployment

- Build the Windows installer with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1` on Windows (use the Windows path to the script from WSL). Requires .NET 8 SDK and Inno Setup 6.
- The output is `dist/ClippedImageToPath-Setup.exe`, with `dist/SHA256SUMS.txt`. GitHub releases ship these files instead of the standalone application EXE.
- Default to a current-user installation without elevation at `%LOCALAPPDATA%\Programs\Clipped Image to Path`.
- The installed EXE is `%LOCALAPPDATA%\Programs\Clipped Image to Path\ClippedImageToPath.exe`. For the local Windows account this is `C:\Users\Standfest\AppData\Local\Programs\Clipped Image to Path\ClippedImageToPath.exe`.
- Deploy through the installer. Stop the running app first, run setup, and restart the installed EXE. Do not deploy to or restart the old source-tree `bin` copy.
- Preserve existing sign-in behavior by selecting the installer's `startup` task when a startup shortcut already exists. Shortcuts must target the installed EXE.
- Keep user settings in `%APPDATA%\ClippedImageToPath`; upgrades and uninstall must preserve them.
- Setup and uninstall close the app through `WM_CLOSE` on `ClippedImageToPathListener`, then wait for its mutex to be released. Keep this wired to the same clean exit as the tray menu. Setup uses `PrepareToInstall` and restarts a previously running app after upgrade. Do not add an `AppMutex` directive that prompts before automatic shutdown.
- Label the installer autostart task "Start with Windows".
- About must show the actual running EXE path from `Application.ExecutablePath`, not a hard-coded installation path.

## GitHub updates

- Default to checking immediately on startup and daily while running. Settings must support hourly, daily, weekly, calendar-monthly, and never. Never disables startup and scheduled checks while leaving manual checks available. Keep checks asynchronous and prevent overlapping checks or downloads.
- Offer Update/Later for a newer stable release with `ClippedImageToPath-Setup.exe`. Download only after Update is clicked. Verify the GitHub SHA-256 digest or published `SHA256SUMS.txt` before launching setup.
- Keep background failures quiet. Manual Check for updates lives in Settings, not the tray menu, and must report results. Later suppresses that version for the current session; manual checks may offer it again.
- Run updater tests with `dotnet run --project tests/Updater.Tests.csproj -c Release`.

## Settings layout

- Group related controls into Saved images, Clipboard, Remote upload, and Updates tabs. Keep the compact window free of scrolling and Save/Cancel in a separate footer.
- Open output folder belongs beside the folder selector in Saved images, not in the tray menu. It opens the path currently shown in the selector.
- Use native layout containers to prevent labels and inputs from overlapping. Preserve tooltips, dependencies, saved values, and keyboard navigation.

## About focus

- Show the app icon before the app name. Keep the EXE path selectable for copying, but focus Close initially so the path is not preselected.
- Keep shared UI conventions documented in `/home/standfest/sites/alexlv.com/docs/windows-apps-common-standards.md` and its published copy when changing them.
