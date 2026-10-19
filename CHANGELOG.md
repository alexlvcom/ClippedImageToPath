# Changelog

## 1.7.1 - 2026-10-19

- Always show the checked Start Clipped Image to Path option when setup finishes, including upgrades, so the app starts after setup.

## 1.7.0 - 2026-10-17

### Main changes

- **Windows installer:** replace the standalone EXE with setup that uses standard Windows installation folders, adds Start menu and optional desktop/startup shortcuts, and supports uninstall through Windows Settings. Existing settings stay intact during upgrades and uninstall.
- **Automatic updates:** check GitHub at startup and daily by default. Choose hourly, daily, weekly, monthly, or never. Choose Update or Later when a newer release is available; Update downloads and verifies the installer before opening setup.
- **Automatic app shutdown:** setup and uninstall close the running app without asking you to exit it manually. Setup restarts it after an upgrade.
- **Tabbed Settings:** organize options into compact Saved images, Clipboard, Remote upload, and Updates tabs without scrolling.

### Other updates

- Move Open output folder beside Browse in Saved images, and put Check for updates beside the update frequency in Updates. Remove both actions from the tray menu.
- Fix overlapping remote upload reminder controls.
- Show the current version in parentheses in the Settings title and in update-check messages. Small result popups show the version only in their message.
- Show the app icon and selectable executable path in About. Focus Close initially so the path is not preselected.
- Use the familiar installer option label Start with Windows.

## 1.6.4 - 2026-10-09

- Paste the saved path with `Shift+Insert` again after copying a screenshot that was already saved,
  or picking it from clipboard history, instead of pasting nothing.

## 1.6.3 - 2026-10-02

- Double-click the tray icon to turn remote upload on or off.
- Show a notification naming the active server when remote upload is turned on from the tray.

## 1.6.2 - 2026-09-29

- Add an application icon to the executable so it displays properly in the Start menu, Windows Search, Taskbar, and File Explorer.
- Set the application icon for all dialog windows.
- Set assembly title and file description metadata to "Clipped Image to Path" so the application displays its spaced name in the Start menu and Windows Search.

## 1.6.1 - 2026-09-16

- Call the app "Clipped Image to Path" in every user-visible string: the About window, the
  Settings title, the tray tooltip and all dialog captions.
- Keep the one-word `ClippedImageToPath` where it is an identifier: the executable, the
  GitHub repository, and the `%APPDATA%\ClippedImageToPath` settings folder.
- Move the GitHub repository from `alexlvcom/clipped-image-to-path` to
  `alexlvcom/ClippedImageToPath`; old links still redirect.

## 1.6.0 - 2026-09-04

- Add settings to turn remote upload notifications on or off and choose their interval in minutes.
- Keep notifications enabled at a 30-minute interval by default.
- Disable notification controls while remote upload is turned off.
- Describe every Settings option with a tooltip when the pointer hovers over it.
- Explain that remote upload notifications are reminders to disable uploading when it is no longer needed.
- Describe remote uploads without implying that source images must already be PNG files.

## 1.5.0 - 2026-09-04

- Show a brief Windows notification every 30 minutes while remote upload is enabled.

## 1.4.0 - 2026-08-10

- Keep copied screenshots as real image clipboard content for normal `Ctrl+V` pastes in Teams, Gmail, and other GUI applications.
- Paste the saved, quoted PNG path with `Shift+Insert`, including in terminals hosted inside editors or other applications.
- Make the path available as soon as the screenshot is saved and restore the image only after the paste shortcut is released.
- Preserve newer clipboard content and recover safely if a key-release event is missed.
