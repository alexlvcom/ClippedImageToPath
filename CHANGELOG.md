# Changelog

## 1.8.0 - 2026-10-20

### Main changes

- **Modern redesign:** Settings, Remote Servers, the server editor, the update dialog, every message and the tray menu now use the modern look shared by all AlexLV Windows apps: flat cards, toggle switches, themed title bars and the green accent of the app icon.
- **Light and dark themes:** a new Appearance tab offers Theme with System (default), Light and Dark. System follows the Windows light or dark mode and switches live when Windows changes; a new choice previews at once and Cancel restores the saved theme.
- **About and Diagnostics in Settings:** About moved from the tray menu into Settings with release notes for the running version, and a Diagnostics tab shows the executable, settings and log paths with Open buttons and Copy details. Every Settings tab fits its window without scrolling.

### Other updates

- A taskbar pin made from an older copy of the app now opens the installed version instead.
- Check for updates in Settings shows its result in the Updates card instead of a pop-up: "You're running the latest version (v1.8.0)" with the check time, or why the check failed. Only a newer release opens the Update dialog. When Settings opens, the card shows what the last check actually found, with an Update button when a newer version was found.
- The server editor shows each problem under its field instead of in a pop-up, and passive mode is greyed out for SFTP, where it does not apply.
- The Updates tab shows when the last check ran, and the update dialog shows download progress and offers Try again after a timeout or failure.
- The Remote upload tab names the server uploads go to and how many servers are configured.
- Windows open already themed instead of flashing light first.

## 1.7.3 - 2026-10-20

### Main changes

- **Settings only change on Save:** if a setting can't be saved, Settings now shows the error and stays open with your edits instead of closing. Cancel, Esc, or closing the window discards every unsaved change.
- **Remote servers use Save and Cancel:** adding, editing, removing, or selecting the active server applies only when you click Save in the Remote Servers window. Cancel discards those edits.

### Other updates

- Save the settings file atomically so an interrupted write cannot leave it half-written.
- Apply a changed remote upload reminder interval as soon as Settings is saved.

## 1.7.2 - 2026-10-19

- Start the update download timeout when Update is clicked. Report timeouts and allow retrying instead of leaving the download dialog stuck.

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
