using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using AlexLV.WindowsUi;
using ClippedImageToPath.Ui;
using FluentFTP;
using Renci.SshNet;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--make-ico")
        {
            TrayIconFactory.SaveApplicationIcon(args.Length > 1 ? args[1] : "ClippedImageToPath.ico");
            return;
        }

        // Developer check: render every window in both themes to PNGs (runs beside the tray instance).
        if (args.Length > 1 && args[0] == "--render-windows")
        {
            Environment.ExitCode = WindowRenderer.Run(args[1]);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, name: "ClippedImageToPath.Singleton", createdNew: out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        ClipboardBridgeContext? running = null;
        Application.ThreadException += (_, e) => running?.LogError("unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => running?.LogError("unhandled exception", e.ExceptionObject as Exception);
        using var context = new ClipboardBridgeContext();
        running = context;
        Application.Run(context);
    }
}

/// <summary>
/// Owns the tray icon, the clipboard listener and the paste hook. The tray and the message loop stay
/// WinForms; every window is WPF with the shared AlexLV.WindowsUi kit.
/// </summary>
internal sealed class ClipboardBridgeContext : ApplicationContext
{
    private const string AppName = AppInfo.DisplayName;

    // Config
    private const string FilePrefix = "clipboard_";
    private const string FileExtension = ".png";
    private const int DebounceMs = 300;
    private const int ClipboardRetryCount = 8;
    private const int ClipboardRetryDelayMs = 40;
    private const int DeferredRetryIntervalMs = 150;
    private const int DeferredRetryMaxAttempts = 12;
    private const int ClipboardInjectDelayMs = 500;
    private const int ClipboardRestoreAfterKeyUpMs = 100;
    private const int ClipboardRestoreSafetyTimeoutMs = 2000;
    private const int SelfInjectIgnoreWindowMs = 3000;
    private static readonly int KeepLatestN = 500;
    private static readonly bool WriteLog = true;

    // State
    private DateTime _lastHandledUtc = DateTime.MinValue;
    private const int RecentSavedImageLimit = 100;
    private bool _isHandling;
    private int _deferredAttemptsRemaining;

    private readonly AppSettings _settings;
    private readonly SynchronizationContext _uiContext;
    private readonly ClipboardListenerWindow _listenerWindow;
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenuStyler _menuStyler;
    private readonly Icon _trayIcon;
    private readonly Icon _remoteUploadTrayIcon;
    private readonly Icon _uploadingTrayIcon;
    private readonly System.Windows.Forms.Timer _deferredProcessTimer;
    private readonly System.Windows.Forms.Timer _clipboardInjectTimer;
    private readonly System.Windows.Forms.Timer _clipboardRestoreTimer;
    private readonly System.Windows.Forms.Timer _uploadStatusTimer;
    private readonly System.Windows.Forms.Timer _remoteUploadReminderTimer;
    private readonly GlobalPasteMonitor? _pasteMonitor;
    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly System.Windows.Forms.Timer _updateTimer;
    private DateTime _nextUpdateCheckUtc;
    private bool _checkingUpdate;
    private Task<UpdateStatus>? _runningCheck;
    private bool _exiting;
    private bool _disposed;
    private Version? _lastOfferedUpdate;
    private SettingsWindow? _settingsWindow;
    private UpdateWindow? _updateWindow;

    private string? _pendingClipboardText;
    private Bitmap? _pendingClipboardImage;
    private string? _lastInjectedClipboardText;
    private string? _lastInjectedImageSha256;
    private string? _activeClipboardPath;
    private Bitmap? _activeClipboardImage;
    private string? _activeClipboardImageSha256;
    private uint _activeClipboardSequence;
    private uint _temporaryPathSequence;
    private readonly Dictionary<string, (string QuotedPath, string FilePath)> _recentSavedImages = new(StringComparer.Ordinal);
    private readonly Queue<string> _recentSavedImageOrder = new();
    private ToolStripMenuItem? _remoteUploadStatusMenuItem;
    private ToolStripMenuItem? _activeServerMenuItem;
    private bool _suppressMenuCloseOnce;
    private DateTime _lastInjectedUtc = DateTime.MinValue;
    private int _activeUploadCount;
    private DateTime _uploadStartedUtc;

    internal ClipboardBridgeContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = AppSettings.Load();
        EnsureOutputDirectoryExists();

        _trayIcon = TrayIconFactory.CreateNormal();
        _remoteUploadTrayIcon = TrayIconFactory.CreateRemoteUploadEnabled();
        _uploadingTrayIcon = TrayIconFactory.CreateUploading();

        // Before the menu and any window: the kit's brushes and title bars follow this theme.
        ThemeManager.Initialize(AppName, AppInfo.Accent, _settings.Theme);
        // A pin made from an old portable copy keeps launching it; point pins at this installed EXE.
        _ = Task.Run(() => ShortcutRepair.RepairTaskbarPins());
        ThemeManager.WindowIcon = WinFormsHosting.ToImageSource(_trayIcon);

        var menu = BuildMenu();
        _menuStyler = new TrayMenuStyler(menu);
        _menuStyler.SetGlyph(_settingsMenuItem!, "");
        _menuStyler.SetGlyph(_exitMenuItem!, "");
        _notifyIcon = new NotifyIcon
        {
            Icon = GetIdleTrayIcon(),
            Text = GetIdleTrayText(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleRemoteUpload();
            }
        };

        _deferredProcessTimer = new System.Windows.Forms.Timer
        {
            Interval = DeferredRetryIntervalMs,
        };
        _deferredProcessTimer.Tick += (_, _) => OnDeferredRetryTick();

        _clipboardInjectTimer = new System.Windows.Forms.Timer
        {
            Interval = ClipboardInjectDelayMs,
        };
        _clipboardInjectTimer.Tick += (_, _) => OnClipboardInjectTick();

        _clipboardRestoreTimer = new System.Windows.Forms.Timer
        {
            Interval = ClipboardRestoreAfterKeyUpMs,
        };
        _clipboardRestoreTimer.Tick += (_, _) => RestoreImageClipboardAfterTerminalPaste();

        _uploadStatusTimer = new System.Windows.Forms.Timer
        {
            Interval = 100,
        };
        _uploadStatusTimer.Tick += (_, _) => UpdateUploadTrayStatus();

        _remoteUploadReminderTimer = new System.Windows.Forms.Timer
        {
            Interval = GetRemoteUploadReminderIntervalMs(),
        };
        _remoteUploadReminderTimer.Tick += (_, _) => ShowRemoteUploadReminder();
        UpdateRemoteUploadReminderTimer();

        _listenerWindow = new ClipboardListenerWindow(HandleClipboardUpdate, ExitApplication);
        try
        {
            _pasteMonitor = new GlobalPasteMonitor(HandlePasteShortcut, HandlePasteShortcutReleased);
        }
        catch (Exception ex)
        {
            _pasteMonitor = null;
            Log($"smart paste unavailable: {ex.Message}");
        }
        // Poll the due time locally: a calendar month exceeds a WinForms timer's maximum interval.
        _updateTimer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
        _updateTimer.Tick += async (_, _) =>
        {
            if (DateTime.UtcNow < _nextUpdateCheckUtc || _checkingUpdate) return;
            _nextUpdateCheckUtc = UpdateSchedule.NextCheck(DateTime.UtcNow, _settings.UpdateCheckFrequency);
            await CheckForUpdatesAsync(false);
        };
        ApplyUpdateSchedule();
        // Start after construction, as soon as the UI message loop runs.
        _uiContext.Post(async _ => await CheckForUpdatesAsync(false), null);
        WhatsNewWindow.Schedule();
        Log($"started v{Changelog.MarketingVersion}");
    }

    private string LogFilePath => Path.Combine(_settings.OutputDirectory, "bridge.log");

    private ToolStripMenuItem? _settingsMenuItem;
    private ToolStripMenuItem? _exitMenuItem;

    internal void LogError(string context, Exception? ex) => Log($"{context}: {ex}");

    private void EnsureOutputDirectoryExists()
    {
        Directory.CreateDirectory(_settings.OutputDirectory);
    }

    /// <summary>The tray Exit command, and setup/uninstall through the listener's WM_CLOSE.</summary>
    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        Log("exiting");
        _notifyIcon.Visible = false;
        // Closing Settings also closes the Remote Servers and server editor windows it owns.
        _settingsWindow?.Close();
        _updateWindow?.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        // A crash path can dispose twice (ExitThread, then Program.Main); cancelling a disposed source throws.
        if (_disposed) return;
        _disposed = true;
        if (disposing)
        {
            _exiting = true;
            _updateTimer.Stop();
            _updateTimer.Dispose();
            _updateCancellation.Cancel();
            _updateCancellation.Dispose();
            _updates.Dispose();
            _listenerWindow.Dispose();
            _deferredProcessTimer.Stop();
            _deferredProcessTimer.Dispose();
            _clipboardInjectTimer.Stop();
            _clipboardInjectTimer.Dispose();
            _clipboardRestoreTimer.Stop();
            _clipboardRestoreTimer.Dispose();
            _uploadStatusTimer.Stop();
            _uploadStatusTimer.Dispose();
            _remoteUploadReminderTimer.Stop();
            _remoteUploadReminderTimer.Dispose();
            _pasteMonitor?.Dispose();
            _pendingClipboardImage?.Dispose();
            _activeClipboardImage?.Dispose();
            _notifyIcon.Visible = false;
            _menuStyler.Dispose();
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _trayIcon.Dispose();
            _remoteUploadTrayIcon.Dispose();
            _uploadingTrayIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        _remoteUploadStatusMenuItem = new ToolStripMenuItem("Enable &remote upload")
        {
            CheckOnClick = false,
        };
        _remoteUploadStatusMenuItem.Click += (_, _) =>
        {
            _suppressMenuCloseOnce = true;
            ToggleRemoteUpload();
        };
        UpdateRemoteUploadStatusMenuItem();
        menu.Items.Add(_remoteUploadStatusMenuItem);

        _activeServerMenuItem = new ToolStripMenuItem("Active server");
        menu.Items.Add(_activeServerMenuItem);
        menu.Opening += (_, _) => RebuildActiveServerMenu();

        // Keep the menu open when the user toggles remote upload, so they can see the state flip.
        menu.Closing += (_, e) =>
        {
            if (_suppressMenuCloseOnce && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
            {
                e.Cancel = true;
            }

            _suppressMenuCloseOnce = false;
        };

        menu.Items.Add(new ToolStripSeparator());
        // About lives in Settings (About and Diagnostics tabs), so the menu has one Settings… item.
        _settingsMenuItem = new ToolStripMenuItem("Se&ttings…", null, (_, _) => ShowSettings());
        _exitMenuItem = new ToolStripMenuItem("E&xit", null, (_, _) => ExitApplication());
        menu.Items.Add(_settingsMenuItem);
        menu.Items.Add(_exitMenuItem);
        return menu;
    }

    private void ApplyUpdateSchedule()
    {
        _settings.UpdateCheckFrequency = UpdateSchedule.Normalize(_settings.UpdateCheckFrequency);
        _updateTimer.Stop();
        _nextUpdateCheckUtc = UpdateSchedule.NextCheck(DateTime.UtcNow, _settings.UpdateCheckFrequency);
        if (_settings.UpdateCheckFrequency != UpdateCheckFrequency.Never) _updateTimer.Start();
    }

    /// <summary>
    /// Checks GitHub. The result is shown in place in Settings → Updates; only a newer release opens a
    /// dialog. Background checks stay quiet otherwise.
    /// </summary>
    private async Task<UpdateStatus> CheckForUpdatesAsync(bool manual)
    {
        var current = Changelog.MarketingVersion;
        // A manual click during a background check reports that check's result instead of "Checking…".
        if (_runningCheck is { IsCompleted: false } running)
        {
            return manual ? await running : new UpdateStatus(UpdateState.Checking, "Checking for updates…", "A check is already running");
        }
        if (_exiting) return new UpdateStatus(UpdateState.NotChecked, "Not checked", $"Version {current}");
        if (!manual && _settings.UpdateCheckFrequency == UpdateCheckFrequency.Never)
        {
            return new UpdateStatus(UpdateState.NotChecked, "Automatic checks are off", $"Version {current}");
        }

        _checkingUpdate = true;
        _runningCheck = RunUpdateCheckAsync(current, manual);
        try { return await _runningCheck; }
        finally { _checkingUpdate = false; }
    }

    private async Task<UpdateStatus> RunUpdateCheckAsync(string current, bool manual)
    {
        try
        {
            var update = await _updates.CheckAsync(Version.Parse(current), _updateCancellation.Token);
            if (_exiting) return new UpdateStatus(UpdateState.NotChecked, "Not checked", $"Version {current}");
            RememberUpdateCheck(current, update is null ? UpdateCheckOutcome.Current : UpdateCheckOutcome.Available,
                update?.Version.ToString(3), error: null);
            var checkedAt = UpdateMessages.LastChecked(DateTime.Now);

            if (update is null) return new UpdateStatus(UpdateState.UpToDate, UpdateMessages.Latest(current), checkedAt);

            var available = new UpdateStatus(UpdateState.Available, UpdateMessages.Available(update.Version.ToString(3)),
                $"You have {current} · {checkedAt}");
            if (!manual && _lastOfferedUpdate == update.Version) return available;
            _lastOfferedUpdate = update.Version;
            // Post the offer so the status card shows the result first.
            _uiContext.Post(_ => ShowUpdateDialog(update), null);
            return available;
        }
        catch (OperationCanceledException) when (_exiting)
        {
            return new UpdateStatus(UpdateState.NotChecked, "Not checked", $"Version {current}");
        }
        catch (Exception ex)
        {
            Log($"update check failed: {ex.Message}");
            if (_exiting) return new UpdateStatus(UpdateState.NotChecked, "Not checked", $"Version {current}");
            RememberUpdateCheck(current, UpdateCheckOutcome.Failed, available: null, ex.Message);
            return new UpdateStatus(UpdateState.Failed, "Could not check for updates", $"Version {current} · {ex.Message}");
        }
    }

    /// <summary>Keeps the last check's outcome so Settings can show it when it next opens.</summary>
    private void RememberUpdateCheck(string current, UpdateCheckOutcome outcome, string? available, string? error)
    {
        _settings.LastUpdateCheckUtc = DateTime.UtcNow;
        _settings.LastUpdateOutcome = outcome;
        _settings.LastUpdateCheckVersion = current;
        _settings.LastAvailableVersion = available;
        _settings.LastUpdateError = error;
        TrySaveSettings("update check result");
    }

    private void ShowUpdateDialog(AppUpdate update)
    {
        if (_exiting) return;
        // An open offer refreshes to the latest release when Update is clicked, so bring it forward instead of stacking another.
        if (_updateWindow is { } open)
        {
            WinFormsHosting.ShowModeless(open);
            return;
        }

        var window = new UpdateWindow(update, _updates, _updateCancellation.Token, Log);
        window.Closed += (_, _) => { if (_updateWindow == window) _updateWindow = null; };
        _updateWindow = window;
        // Modeless, so an unanswered offer never disables Settings or blocks later checks.
        WinFormsHosting.ShowModeless(window);
    }

    /// <summary>Saves background state (toggles, check time) without failing the caller.</summary>
    private void TrySaveSettings(string what)
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex)
        {
            Log($"saving {what} failed: {ex.Message}");
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            WinFormsHosting.ShowModeless(_settingsWindow);
            return;
        }

        var host = new SettingsHost(
            SmartPasteAvailable: _pasteMonitor is not null,
            FileNameExample: $"{FilePrefix}{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}{FileExtension}",
            KeepLatest: KeepLatestN,
            LogFilePath: LogFilePath,
            Running: () => _settings,
            Save: SaveSettingsDraft,
            CheckForUpdates: () => CheckForUpdatesAsync(true),
            ManageServers: ShowRemoteServers);

        // A copy, so Cancel really cancels.
        _settingsWindow = new SettingsWindow(_settings.Clone(), host);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        WinFormsHosting.ShowModeless(_settingsWindow);
    }

    /// <summary>
    /// Commits the Settings draft: validates and writes before the window closes, so a failure keeps it
    /// open with the edits and leaves the running settings unchanged. Returns the error, or null.
    /// </summary>
    private string? SaveSettingsDraft(AppSettings draft)
    {
        var previous = (
            _settings.OutputDirectory, _settings.ConvertToWslPath, _settings.SmartPasteEnabled,
            _settings.RemoteUploadEnabled, _settings.RemoteUploadReminderEnabled,
            _settings.RemoteUploadReminderMinutes, _settings.UpdateCheckFrequency, _settings.Theme);
        try
        {
            var fullFolder = Path.GetFullPath(draft.OutputDirectory);
            Directory.CreateDirectory(fullFolder);
            _settings.OutputDirectory = fullFolder;
            _settings.ConvertToWslPath = draft.ConvertToWslPath;
            _settings.SmartPasteEnabled = draft.SmartPasteEnabled;
            _settings.RemoteUploadEnabled = draft.RemoteUploadEnabled;
            _settings.RemoteUploadReminderEnabled = draft.RemoteUploadReminderEnabled;
            _settings.RemoteUploadReminderMinutes = draft.RemoteUploadReminderMinutes;
            _settings.UpdateCheckFrequency = draft.UpdateCheckFrequency;
            _settings.Theme = draft.Theme;
            _settings.Save();
        }
        catch (Exception ex)
        {
            (_settings.OutputDirectory, _settings.ConvertToWslPath, _settings.SmartPasteEnabled,
                _settings.RemoteUploadEnabled, _settings.RemoteUploadReminderEnabled,
                _settings.RemoteUploadReminderMinutes, _settings.UpdateCheckFrequency, _settings.Theme) = previous;
            Log($"settings save failed: {ex.Message}");
            return ex.Message;
        }

        ApplyUpdateSchedule();
        UpdateRemoteUploadReminderTimer();
        RefreshIdleTrayIcon();
        ThemeManager.Apply(_settings.Theme);
        Log("settings updated");
        return null;
    }

    private void ShowRemoteServers(System.Windows.Window owner)
    {
        var window = new RemoteServersWindow(_settings, (servers, active) =>
        {
            var previousServers = _settings.RemoteServers.ToList();
            var previousActive = _settings.ActiveServer;
            _settings.RemoteServers = servers.Select(s => s.Clone()).ToList();
            _settings.ActiveServer = active;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                _settings.RemoteServers = previousServers;
                _settings.ActiveServer = previousActive;
                Log($"remote servers save failed: {ex.Message}");
                return ex.Message;
            }

            Log($"remote servers updated: {servers.Count} server(s), active '{active}'");
            return null;
        }) { Owner = owner };
        window.ShowDialog();
        RefreshIdleTrayIcon();
    }

    private void HandleClipboardUpdate()
    {
        MaybeCancelPendingClipboardInject();

        if (_isHandling)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        if ((nowUtc - _lastHandledUtc).TotalMilliseconds < DebounceMs)
        {
            return;
        }

        _isHandling = true;
        try
        {
            _lastHandledUtc = nowUtc;
            EnsureOutputDirectoryExists();
            var outputDirectory = _settings.OutputDirectory;

            var existingText = TryGetClipboardText();
            if (IsRecentOwnInjectedClipboardText(existingText))
            {
                return;
            }

            if (IsOwnOutputPathText(existingText, outputDirectory))
            {
                return;
            }

            using var image = TryGetClipboardImage();
            if (image is null)
            {
                if (!TryClipboardHasText())
                {
                    StartDeferredRetry();
                }
                return;
            }

            StopDeferredRetry();

            var pngBytes = EncodePng(image);
            var imageHash = ComputeSha256Hex(pngBytes);
            if (IsRecentOwnInjectedImageHash(imageHash) || TryReuseSavedImage(image, imageHash))
            {
                return;
            }

            var filePath = BuildUniqueFilePath(outputDirectory);
            File.WriteAllBytes(filePath, pngBytes);

            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length == 0)
            {
                throw new IOException($"Saved file is missing or empty: {filePath}");
            }

            var clipboardPath = _settings.ConvertToWslPath
                ? ConvertWindowsPathToWsl(filePath)
                : filePath;
            var quotedPath = QuoteForClipboard(clipboardPath);
            RememberSavedImage(imageHash, quotedPath, filePath);
            ScheduleClipboardInject(quotedPath, image);

            CleanupOldFilesIfNeeded(outputDirectory);
            Log($"saved {filePath}");
            QueueRemoteUploadIfEnabled(filePath);
        }
        catch (Exception ex)
        {
            Log($"error {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _isHandling = false;
        }
    }

    private void OnDeferredRetryTick()
    {
        if (_deferredAttemptsRemaining <= 0)
        {
            StopDeferredRetry();
            return;
        }

        _deferredAttemptsRemaining--;
        HandleClipboardUpdateIgnoringDebounce();
    }

    private void HandleClipboardUpdateIgnoringDebounce()
    {
        if (_isHandling)
        {
            return;
        }

        _isHandling = true;
        try
        {
            EnsureOutputDirectoryExists();
            var outputDirectory = _settings.OutputDirectory;

            var existingText = TryGetClipboardText();
            if (IsRecentOwnInjectedClipboardText(existingText))
            {
                StopDeferredRetry();
                return;
            }

            if (IsOwnOutputPathText(existingText, outputDirectory))
            {
                StopDeferredRetry();
                return;
            }

            using var image = TryGetClipboardImage();
            if (image is null)
            {
                if (_deferredAttemptsRemaining <= 0)
                {
                    StopDeferredRetry();
                }
                return;
            }

            StopDeferredRetry();

            var pngBytes = EncodePng(image);
            var imageHash = ComputeSha256Hex(pngBytes);
            if (IsRecentOwnInjectedImageHash(imageHash) || TryReuseSavedImage(image, imageHash))
            {
                return;
            }

            var filePath = BuildUniqueFilePath(outputDirectory);
            File.WriteAllBytes(filePath, pngBytes);

            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length == 0)
            {
                throw new IOException($"Saved file is missing or empty: {filePath}");
            }

            var clipboardPath = _settings.ConvertToWslPath
                ? ConvertWindowsPathToWsl(filePath)
                : filePath;
            var quotedPath = QuoteForClipboard(clipboardPath);
            RememberSavedImage(imageHash, quotedPath, filePath);
            ScheduleClipboardInject(quotedPath, image);
            CleanupOldFilesIfNeeded(outputDirectory);
            Log($"saved {filePath}");
            QueueRemoteUploadIfEnabled(filePath);
        }
        catch (Exception ex)
        {
            Log($"error {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _isHandling = false;
        }
    }

    private void StartDeferredRetry()
    {
        if (_deferredAttemptsRemaining < DeferredRetryMaxAttempts)
        {
            _deferredAttemptsRemaining = DeferredRetryMaxAttempts;
        }

        if (!_deferredProcessTimer.Enabled)
        {
            _deferredProcessTimer.Start();
        }
    }

    private void StopDeferredRetry()
    {
        _deferredAttemptsRemaining = 0;
        if (_deferredProcessTimer.Enabled)
        {
            _deferredProcessTimer.Stop();
        }
    }

    private void QueueRemoteUploadIfEnabled(string filePath)
    {
        if (!_settings.RemoteUploadEnabled)
        {
            return;
        }

        if (!_settings.TryGetRemoteUploadConfig(out var config, out var error))
        {
            Log($"upload skipped: {error}");
            return;
        }

        BeginUploadStatus();
        _ = Task.Run(() =>
        {
            try
            {
                var remotePath = RemoteUploader.UploadFile(filePath, config);
                Log($"uploaded {filePath} -> {remotePath}");
            }
            catch (Exception ex)
            {
                Log($"upload error {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                FinishUploadStatus();
            }
        });
    }

    private void BeginUploadStatus()
    {
        _uiContext.Post(_ =>
        {
            _activeUploadCount++;
            if (_activeUploadCount == 1)
            {
                _uploadStartedUtc = DateTime.UtcNow;
                _notifyIcon.Icon = _uploadingTrayIcon;
                _uploadStatusTimer.Start();
            }

            UpdateUploadTrayStatus();
        }, null);
    }

    private void FinishUploadStatus()
    {
        _uiContext.Post(_ =>
        {
            if (_activeUploadCount > 0)
            {
                _activeUploadCount--;
            }

            if (_activeUploadCount == 0)
            {
                _uploadStatusTimer.Stop();
                _notifyIcon.Icon = GetIdleTrayIcon();
                _notifyIcon.Text = GetIdleTrayText();
            }
        }, null);
    }

    private Icon GetIdleTrayIcon()
    {
        return _settings.RemoteUploadEnabled ? _remoteUploadTrayIcon : _trayIcon;
    }

    private void RefreshIdleTrayIcon()
    {
        UpdateRemoteUploadStatusMenuItem();
        UpdateRemoteUploadReminderTimer();
        if (_activeUploadCount == 0)
        {
            _notifyIcon.Icon = GetIdleTrayIcon();
            _notifyIcon.Text = GetIdleTrayText();
        }
    }

    private string GetIdleTrayText()
    {
        if (!_settings.RemoteUploadEnabled)
        {
            return AppName;
        }

        var active = _settings.GetActiveServer();
        var text = active is null
            ? $"{AppName} - Remote upload enabled"
            : $"{AppName} - Upload to {active.Name}";
        return text.Length <= 63 ? text : text[..63];
    }

    private void ToggleRemoteUpload()
    {
        _settings.RemoteUploadEnabled = !_settings.RemoteUploadEnabled;
        TrySaveSettings("remote upload toggle");
        RefreshIdleTrayIcon();
        RebuildActiveServerMenu();
        Log($"remote upload {(_settings.RemoteUploadEnabled ? "enabled" : "disabled")}");

        if (_settings.RemoteUploadEnabled)
        {
            var active = _settings.GetActiveServer();
            _notifyIcon.ShowBalloonTip(
                3000,
                "Remote upload enabled",
                active is null
                    ? "Remote upload is enabled, but no server is selected."
                    : $"Remote upload to {active.Name} is enabled.",
                ToolTipIcon.Info);
        }
    }

    private void UpdateRemoteUploadReminderTimer()
    {
        if (!_settings.RemoteUploadEnabled || !_settings.RemoteUploadReminderEnabled)
        {
            _remoteUploadReminderTimer.Stop();
            return;
        }

        var interval = GetRemoteUploadReminderIntervalMs();
        if (_remoteUploadReminderTimer.Interval != interval)
        {
            _remoteUploadReminderTimer.Stop();
            _remoteUploadReminderTimer.Interval = interval;
        }

        if (!_remoteUploadReminderTimer.Enabled)
        {
            _remoteUploadReminderTimer.Start();
        }
    }

    private int GetRemoteUploadReminderIntervalMs()
    {
        return _settings.RemoteUploadReminderMinutes * 60 * 1000;
    }

    private void ShowRemoteUploadReminder()
    {
        if (!_settings.RemoteUploadEnabled || !_settings.RemoteUploadReminderEnabled)
        {
            _remoteUploadReminderTimer.Stop();
            return;
        }

        _notifyIcon.ShowBalloonTip(
            5000,
            "Remote upload reminder",
            "Remote upload is still enabled. Disable it when you no longer need it.",
            ToolTipIcon.Info);
    }

    private void UpdateRemoteUploadStatusMenuItem()
    {
        if (_remoteUploadStatusMenuItem is not null)
        {
            _remoteUploadStatusMenuItem.Checked = _settings.RemoteUploadEnabled;
        }
    }

    private void RebuildActiveServerMenu()
    {
        if (_activeServerMenuItem is null)
        {
            return;
        }

        // Active server can only be changed while remote upload is enabled.
        _activeServerMenuItem.Enabled = _settings.RemoteUploadEnabled && _settings.RemoteServers.Count > 0;

        _activeServerMenuItem.DropDownItems.Clear();

        if (_settings.RemoteServers.Count == 0)
        {
            _activeServerMenuItem.Text = "Active server: (none)";
            _activeServerMenuItem.DropDownItems.Add(new ToolStripMenuItem("(no servers configured)") { Enabled = false });
            StyleActiveServerMenu();
            return;
        }

        var active = _settings.GetActiveServer();
        _activeServerMenuItem.Text = $"Active server: {active?.Name ?? "(none)"}";

        foreach (var server in _settings.RemoteServers)
        {
            var captured = server;
            var item = new ToolStripMenuItem(server.Name)
            {
                Checked = ReferenceEquals(server, active),
            };
            item.Click += (_, _) =>
            {
                _settings.ActiveServer = captured.Name;
                TrySaveSettings("active server");
                RefreshIdleTrayIcon();
                Log($"active server -> {captured.Name}");
            };
            _activeServerMenuItem.DropDownItems.Add(item);
        }

        StyleActiveServerMenu();
    }

    // The kit styles the top-level tray menu; draw the submenu with the same renderer and colors.
    private void StyleActiveServerMenu()
    {
        if (_activeServerMenuItem is null || _notifyIcon?.ContextMenuStrip is not { } menu) return;
        var dropDown = _activeServerMenuItem.DropDown;
        dropDown.Renderer = menu.Renderer;
        dropDown.BackColor = menu.BackColor;
        dropDown.Font = menu.Font;
        dropDown.Padding = menu.Padding;
        foreach (ToolStripItem item in dropDown.Items)
        {
            item.ForeColor = menu.ForeColor;
            item.Padding = new Padding(2, 5, 2, 5);
        }

        if (!_styledDropDowns.Contains(dropDown))
        {
            _styledDropDowns.Add(dropDown);
            dropDown.Opened += (_, _) => ThemeManager.RoundCorners(dropDown.Handle);
        }
    }

    private readonly HashSet<ToolStripDropDown> _styledDropDowns = new();

    private void UpdateUploadTrayStatus()
    {
        if (_activeUploadCount <= 0)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - _uploadStartedUtc;
        var text = $"{AppName} uploading {elapsed.TotalSeconds:0.0}s";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void ScheduleClipboardInject(string text, Image image)
    {
        _lastInjectedClipboardText = text;
        _lastInjectedImageSha256 = ComputeSha256Hex(EncodePng(image));
        _lastInjectedUtc = DateTime.UtcNow;

        _activeClipboardImage?.Dispose();
        _activeClipboardImage = new Bitmap(image);
        _activeClipboardPath = text;
        _activeClipboardImageSha256 = _lastInjectedImageSha256;
        _activeClipboardSequence = NativeMethods.GetClipboardSequenceNumber();

        _pendingClipboardImage?.Dispose();
        _pendingClipboardImage = new Bitmap(image);
        _pendingClipboardText = text;
        _clipboardInjectTimer.Stop();
        _clipboardInjectTimer.Start();
    }

    private void CancelPendingClipboardInject()
    {
        _clipboardInjectTimer.Stop();
        _pendingClipboardImage?.Dispose();
        _pendingClipboardImage = null;
        _pendingClipboardText = null;
    }

    private void MaybeCancelPendingClipboardInject()
    {
        if (_pendingClipboardImage is null || string.IsNullOrWhiteSpace(_pendingClipboardText))
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsImage())
            {
                return;
            }

            var text = TryGetClipboardText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (IsOwnOutputPathText(text, _settings.OutputDirectory))
            {
                return;
            }

            // Clipboard changed to other text content before delayed injection fired.
            CancelPendingClipboardInject();
        }
        catch (ExternalException)
        {
            // Keep pending state and try again on next update.
        }
    }

    private bool IsRecentOwnInjectedClipboardText(string? text)
    {
        if ((DateTime.UtcNow - _lastInjectedUtc).TotalMilliseconds > SelfInjectIgnoreWindowMs)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(_lastInjectedClipboardText))
        {
            return false;
        }

        return string.Equals(text.Trim(), _lastInjectedClipboardText, StringComparison.Ordinal);
    }

    private bool IsRecentOwnInjectedImageHash(string imageHash)
    {
        if ((DateTime.UtcNow - _lastInjectedUtc).TotalMilliseconds > SelfInjectIgnoreWindowMs)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_lastInjectedImageSha256))
        {
            return false;
        }

        return string.Equals(imageHash, _lastInjectedImageSha256, StringComparison.Ordinal);
    }

    private void OnClipboardInjectTick()
    {
        _clipboardInjectTimer.Stop();

        if (_temporaryPathSequence != 0)
        {
            _clipboardInjectTimer.Start();
            return;
        }

        if (_pendingClipboardImage is null || string.IsNullOrWhiteSpace(_pendingClipboardText))
        {
            return;
        }

        try
        {
            if (_activeClipboardImage is null)
            {
                _activeClipboardImage = new Bitmap(_pendingClipboardImage);
                _activeClipboardPath = _pendingClipboardText;
                _activeClipboardImageSha256 = ComputeSha256Hex(EncodePng(_pendingClipboardImage));
            }

            var activePath = _activeClipboardPath ?? _pendingClipboardText;
            var clipboardSet = _settings.SmartPasteEnabled && _pasteMonitor is not null
                ? TrySetClipboardImage(_activeClipboardImage)
                : TrySetClipboardContent(activePath, _activeClipboardImage);
            if (!clipboardSet)
            {
                Log("error IOException: delayed clipboard injection failed");
                ClearActiveClipboardPayload();
            }
            else
            {
                _activeClipboardSequence = NativeMethods.GetClipboardSequenceNumber();
            }
        }
        finally
        {
            _pendingClipboardImage.Dispose();
            _pendingClipboardImage = null;
            _pendingClipboardText = null;
        }
    }

    private void HandlePasteShortcut()
    {
        if (!_settings.SmartPasteEnabled)
        {
            return;
        }

        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (_temporaryPathSequence != 0 && sequence == _temporaryPathSequence)
        {
            return;
        }

        var activeIsCurrent = _activeClipboardImage is not null
            && !string.IsNullOrWhiteSpace(_activeClipboardPath)
            && sequence == _activeClipboardSequence;
        if ((!activeIsCurrent && !TryActivateSavedClipboardImage()) || _activeClipboardPath is not { } activePath)
        {
            ClearActiveClipboardPayload();
            return;
        }

        _lastInjectedClipboardText = activePath;
        _lastInjectedUtc = DateTime.UtcNow;
        if (!TrySetClipboardText(activePath))
        {
            return;
        }

        _temporaryPathSequence = NativeMethods.GetClipboardSequenceNumber();
        _clipboardRestoreTimer.Stop();
        _clipboardRestoreTimer.Interval = ClipboardRestoreSafetyTimeoutMs;
        _clipboardRestoreTimer.Start();
    }

    private void HandlePasteShortcutReleased()
    {
        if (_temporaryPathSequence == 0)
        {
            return;
        }

        _clipboardRestoreTimer.Stop();
        _clipboardRestoreTimer.Interval = ClipboardRestoreAfterKeyUpMs;
        _clipboardRestoreTimer.Start();
    }

    private void RememberSavedImage(string imageHash, string quotedPath, string filePath)
    {
        if (!_recentSavedImages.ContainsKey(imageHash))
        {
            _recentSavedImageOrder.Enqueue(imageHash);
        }

        _recentSavedImages[imageHash] = (quotedPath, filePath);
        while (_recentSavedImageOrder.Count > RecentSavedImageLimit)
        {
            _ = _recentSavedImages.Remove(_recentSavedImageOrder.Dequeue());
        }
    }

    private bool TryGetSavedImagePath(string imageHash, out string quotedPath)
    {
        quotedPath = string.Empty;
        if (!_recentSavedImages.TryGetValue(imageHash, out var saved) || !File.Exists(saved.FilePath))
        {
            return false;
        }

        quotedPath = saved.QuotedPath;
        return true;
    }

    // An image that was already saved came back to the clipboard (copied again or picked from
    // clipboard history), so pair it with its existing file instead of saving a duplicate.
    private bool TryReuseSavedImage(Image image, string imageHash)
    {
        if (!TryGetSavedImagePath(imageHash, out var quotedPath))
        {
            return false;
        }

        if (_settings.SmartPasteEnabled && _pasteMonitor is not null)
        {
            SetActiveClipboardPayload(quotedPath, image, imageHash);
        }
        else
        {
            ScheduleClipboardInject(quotedPath, image);
        }

        return true;
    }

    private bool TryActivateSavedClipboardImage()
    {
        using var image = TryGetClipboardImage();
        if (image is null)
        {
            return false;
        }

        var imageHash = ComputeSha256Hex(EncodePng(image));
        if (!TryGetSavedImagePath(imageHash, out var quotedPath))
        {
            return false;
        }

        SetActiveClipboardPayload(quotedPath, image, imageHash);
        return true;
    }

    private void SetActiveClipboardPayload(string quotedPath, Image image, string imageHash)
    {
        _activeClipboardImage?.Dispose();
        _activeClipboardImage = new Bitmap(image);
        _activeClipboardPath = quotedPath;
        _activeClipboardImageSha256 = imageHash;
        _activeClipboardSequence = NativeMethods.GetClipboardSequenceNumber();
    }

    private void RestoreImageClipboardAfterTerminalPaste()
    {
        _clipboardRestoreTimer.Stop();
        if (_activeClipboardImage is null || NativeMethods.GetClipboardSequenceNumber() != _temporaryPathSequence)
        {
            return;
        }

        _lastInjectedImageSha256 = ComputeSha256Hex(EncodePng(_activeClipboardImage));
        _lastInjectedUtc = DateTime.UtcNow;
        if (TrySetClipboardImage(_activeClipboardImage))
        {
            _activeClipboardSequence = NativeMethods.GetClipboardSequenceNumber();
            _temporaryPathSequence = 0;
        }
    }

    private void ClearActiveClipboardPayload()
    {
        _clipboardRestoreTimer.Stop();
        _activeClipboardImage?.Dispose();
        _activeClipboardImage = null;
        _activeClipboardPath = null;
        _activeClipboardImageSha256 = null;
        _activeClipboardSequence = 0;
        _temporaryPathSequence = 0;
    }

    private static bool TryClipboardHasText()
    {
        try
        {
            return Clipboard.ContainsText();
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static string BuildUniqueFilePath(string outputDirectory)
    {
        var baseName = $"{FilePrefix}{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}";
        var path = Path.Combine(outputDirectory, baseName + FileExtension);

        if (!File.Exists(path))
        {
            return path;
        }

        var suffix = Guid.NewGuid().ToString("N")[..6];
        return Path.Combine(outputDirectory, $"{baseName}_{suffix}{FileExtension}");
    }

    private static byte[] EncodePng(Image image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            _ = sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static string QuoteForClipboard(string path) => $"\"{path}\"";

    private static string ConvertWindowsPathToWsl(string windowsPath)
    {
        var full = Path.GetFullPath(windowsPath);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || root[1] != ':')
        {
            return full.Replace('\\', '/');
        }

        var drive = char.ToLowerInvariant(root[0]);
        var tail = full[root.Length..].Replace('\\', '/');
        return $"/mnt/{drive}/{tail}";
    }

    private static bool IsOwnOutputPathText(string? text, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }

        if (!trimmed.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var full = Path.GetFullPath(trimmed);
        var target = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(target, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.Exists(full);
    }

    private static Image? TryGetClipboardImage()
    {
        for (var i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                if (!Clipboard.ContainsImage())
                {
                    return null;
                }

                var img = Clipboard.GetImage();
                if (img is null)
                {
                    return null;
                }

                return new Bitmap(img);
            }
            catch (ExternalException)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }

        return null;
    }

    private static string? TryGetClipboardText()
    {
        for (var i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (ExternalException)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }

        return null;
    }

    private static bool TrySetClipboardContent(string text, Image image)
    {
        for (var i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                // Keep image data so clipboard managers can still treat this as an image clip,
                // and add text so terminals paste the saved file path.
                var data = new DataObject();
                data.SetText(text, TextDataFormat.UnicodeText);
                data.SetText(text, TextDataFormat.Text);

                using var bmp = new Bitmap(image);
                data.SetImage(bmp);

                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }

        return false;
    }

    private static bool TrySetClipboardImage(Image image)
    {
        for (var i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                var data = new DataObject();
                using var bmp = new Bitmap(image);
                data.SetImage(bmp);
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }

        return false;
    }

    private static bool TrySetClipboardText(string text)
    {
        for (var i = 0; i < ClipboardRetryCount; i++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }

        return false;
    }

    private static void CleanupOldFilesIfNeeded(string outputDirectory)
    {
        if (KeepLatestN <= 0)
        {
            return;
        }

        if (!Directory.Exists(outputDirectory))
        {
            return;
        }

        var files = new DirectoryInfo(outputDirectory)
            .GetFiles($"{FilePrefix}*{FileExtension}", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.CreationTimeUtc)
            .ToList();

        if (files.Count <= KeepLatestN)
        {
            return;
        }

        foreach (var file in files.Skip(KeepLatestN))
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private void Log(string line)
    {
        if (!WriteLog)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_settings.OutputDirectory);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(LogFilePath, $"[{stamp}] {line}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging failures.
        }
    }
}

internal static class RemoteUploader
{
    internal static void TestConnection(RemoteUploadConfig config)
    {
        if (config.Protocol == RemoteProtocol.Sftp)
        {
            SftpTestConnection(config);
        }
        else
        {
            FtpTestConnection(config);
        }
    }

    internal static string UploadFile(string localFilePath, RemoteUploadConfig config)
    {
        return config.Protocol == RemoteProtocol.Sftp
            ? SftpUploadFile(localFilePath, config)
            : FtpUploadFile(localFilePath, config);
    }

    private static void SftpTestConnection(RemoteUploadConfig config)
    {
        using var client = CreateSftpClient(config);
        client.Connect();
        try
        {
            if (!client.Exists(config.RemoteDirectory))
            {
                throw new DirectoryNotFoundException($"Remote directory does not exist: {config.RemoteDirectory}");
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static string SftpUploadFile(string localFilePath, RemoteUploadConfig config)
    {
        var fileName = Path.GetFileName(localFilePath);
        var remotePath = CombineRemotePath(config.RemoteDirectory, fileName);

        using var client = CreateSftpClient(config);
        client.Connect();
        try
        {
            if (!client.Exists(config.RemoteDirectory))
            {
                throw new DirectoryNotFoundException($"Remote directory does not exist: {config.RemoteDirectory}");
            }

            using var stream = File.OpenRead(localFilePath);
            client.UploadFile(stream, remotePath, true);
            return remotePath;
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static SftpClient CreateSftpClient(RemoteUploadConfig config)
    {
        var client = new SftpClient(config.Host, config.Port, config.User, config.Password)
        {
            OperationTimeout = TimeSpan.FromSeconds(30),
            KeepAliveInterval = TimeSpan.FromSeconds(15),
        };
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private static void FtpTestConnection(RemoteUploadConfig config)
    {
        using var client = CreateFtpClient(config);
        client.Connect();
        try
        {
            if (!client.DirectoryExists(config.RemoteDirectory))
            {
                throw new DirectoryNotFoundException($"Remote directory does not exist: {config.RemoteDirectory}");
            }
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static string FtpUploadFile(string localFilePath, RemoteUploadConfig config)
    {
        var fileName = Path.GetFileName(localFilePath);
        var remotePath = CombineRemotePath(config.RemoteDirectory, fileName);

        using var client = CreateFtpClient(config);
        client.Connect();
        try
        {
            if (!client.DirectoryExists(config.RemoteDirectory))
            {
                throw new DirectoryNotFoundException($"Remote directory does not exist: {config.RemoteDirectory}");
            }

            var status = client.UploadFile(localFilePath, remotePath, FtpRemoteExists.Overwrite, createRemoteDir: false);
            if (status == FtpStatus.Failed)
            {
                throw new IOException($"FTP upload failed: {remotePath}");
            }

            return remotePath;
        }
        finally
        {
            client.Disconnect();
        }
    }

    private static FtpClient CreateFtpClient(RemoteUploadConfig config)
    {
        var client = new FtpClient(config.Host, config.User, config.Password, config.Port);
        client.Config.EncryptionMode = config.Protocol switch
        {
            RemoteProtocol.FtpsExplicit => FtpEncryptionMode.Explicit,
            RemoteProtocol.FtpsImplicit => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None,
        };

        // Local FileZilla servers typically use a self-signed certificate, so trust any presented cert.
        client.Config.ValidateAnyCertificate = true;
        client.Config.DataConnectionType = config.PassiveMode
            ? FtpDataConnectionType.AutoPassive
            : FtpDataConnectionType.AutoActive;
        client.Config.ConnectTimeout = 15000;
        client.Config.ReadTimeout = 30000;
        client.Config.DataConnectionConnectTimeout = 15000;
        client.Config.DataConnectionReadTimeout = 30000;
        return client;
    }

    private static string CombineRemotePath(string directory, string fileName)
    {
        var trimmed = directory.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? fileName : $"{trimmed}/{fileName}";
    }
}

internal sealed class ClipboardListenerWindow : NativeWindow, IDisposable
{
    private readonly Action _onClipboardUpdate;
    private readonly Action _onExit;

    internal ClipboardListenerWindow(Action onClipboardUpdate, Action onExit)
    {
        _onClipboardUpdate = onClipboardUpdate;
        _onExit = onExit;

        CreateHandle(new CreateParams
        {
            Caption = "ClippedImageToPathListener",
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            Style = 0,
        });

        if (!NativeMethods.AddClipboardFormatListener(Handle))
        {
            throw new InvalidOperationException("Failed to register clipboard listener.");
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Let the uninstaller use the same clean shutdown as the tray's Exit command.
        if (m.Msg == 0x0010) // WM_CLOSE
        {
            _onExit();
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            _onClipboardUpdate();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }
    }
}

internal sealed class GlobalPasteMonitor : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly Action _onPaste;
    private readonly Action _onPasteReleased;
    private IntPtr _hook;
    private bool _pasteKeyDown;

    internal GlobalPasteMonitor(Action onPaste, Action onPasteReleased)
    {
        _onPaste = onPaste;
        _onPasteReleased = onPasteReleased;
        _callback = HookCallback;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to install the keyboard paste monitor.");
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var key = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam).VirtualKeyCode;
            if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                if (key == NativeMethods.VK_INSERT)
                {
                    var wasPasteKeyDown = _pasteKeyDown;
                    _pasteKeyDown = false;
                    if (wasPasteKeyDown)
                    {
                        try
                        {
                            _onPasteReleased();
                        }
                        catch
                        {
                            // Never let clipboard failures escape through the native hook callback.
                        }
                    }
                }
            }
            else if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                var isPaste = key == NativeMethods.VK_INSERT && NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT);
                if (isPaste && !_pasteKeyDown)
                {
                    _pasteKeyDown = true;
                    try
                    {
                        _onPaste();
                    }
                    catch
                    {
                        // Never let clipboard failures escape through the native hook callback.
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}

internal static class TrayIconFactory
{
    internal static Icon CreateNormal()
    {
        return Create(Color.FromArgb(0, 120, 80), Color.White, drawUploadBadge: false);
    }

    internal static Icon CreateRemoteUploadEnabled()
    {
        return Create(Color.FromArgb(0, 120, 80), Color.FromArgb(255, 210, 0), drawUploadBadge: false);
    }

    internal static Icon CreateUploading()
    {
        return Create(Color.FromArgb(0, 95, 170), Color.White, drawUploadBadge: true);
    }

    internal static void SaveApplicationIcon(string filePath)
    {
        int[] sizes = [16, 20, 24, 30, 32, 40, 48, 64, 128, 256];
        var frames = new byte[sizes.Length][];

        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float scale = s / 32.0f;
                using var bgBrush = new SolidBrush(Color.FromArgb(0, 120, 80));
                FillRoundedRectangle(g, bgBrush, 2f * scale, 2f * scale, 28f * scale, 28f * scale, 7f * scale);

                using var fillBrush = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
                g.FillRectangle(fillBrush, 9f * scale, 8f * scale, 10f * scale, 8f * scale);
                g.FillRectangle(fillBrush, 13f * scale, 18f * scale, 8f * scale, 3f * scale);

                using var pen = new Pen(Color.White, Math.Max(1f, 2f * scale));
                g.DrawRectangle(pen, 8f * scale, 7f * scale, 12f * scale, 10f * scale);
                g.DrawLine(pen, 12f * scale, 20f * scale, 24f * scale, 20f * scale);
                g.DrawLine(pen, 24f * scale, 20f * scale, 24f * scale, 12f * scale);
            }

            if (s >= 128)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                frames[i] = ms.ToArray();
            }
            else
            {
                frames[i] = CreateDibFrame(bmp);
            }
        }

        using var fs = File.Create(filePath);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // type (1 = ICO)
        bw.Write((ushort)sizes.Length); // count

        int offset = 6 + sizes.Length * 16;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s)); // width
            bw.Write((byte)(s >= 256 ? 0 : s)); // height
            bw.Write((byte)0); // color count
            bw.Write((byte)0); // reserved
            bw.Write((ushort)1); // planes
            bw.Write((ushort)32); // bit count
            bw.Write((uint)frames[i].Length); // size
            bw.Write((uint)offset); // offset
            offset += frames[i].Length;
        }

        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(frames[i]);
        }
    }

    private static byte[] CreateDibFrame(Bitmap bmp)
    {
        int width = bmp.Width;
        int height = bmp.Height;
        int maskRowBytes = ((width + 31) / 32) * 4;
        int maskSize = maskRowBytes * height;
        int pixelDataSize = width * height * 4;
        int totalSize = 40 + pixelDataSize + maskSize;

        byte[] dib = new byte[totalSize];
        using var ms = new MemoryStream(dib);
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER
        bw.Write(40);
        bw.Write(width);
        bw.Write(height * 2);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);
        bw.Write(pixelDataSize + maskSize);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        // Pixel data (bottom-to-top BGRA)
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixelBytes = new byte[width * 4];
            for (int y = height - 1; y >= 0; y--)
            {
                IntPtr rowPtr = IntPtr.Add(data.Scan0, y * data.Stride);
                Marshal.Copy(rowPtr, pixelBytes, 0, width * 4);
                bw.Write(pixelBytes, 0, width * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        // AND mask (all zeros for 32bpp)
        byte[] mask = new byte[maskSize];
        bw.Write(mask);

        return dib;
    }

    private static Icon Create(Color backgroundColor, Color foregroundColor, bool drawUploadBadge)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bgBrush = new SolidBrush(backgroundColor);
            FillRoundedRectangle(g, bgBrush, 2, 2, 28, 28, 7);

            using var fillBrush = new SolidBrush(Color.FromArgb(60, foregroundColor));
            g.FillRectangle(fillBrush, 9, 8, 10, 8);
            g.FillRectangle(fillBrush, 13, 18, 8, 3);

            using var pen = new Pen(foregroundColor, 2f);
            g.DrawRectangle(pen, 8, 7, 12, 10);
            g.DrawLine(pen, 12, 20, 24, 20);
            g.DrawLine(pen, 24, 20, 24, 12);

            if (drawUploadBadge)
            {
                using var badgeBrush = new SolidBrush(Color.FromArgb(255, 190, 45));
                g.FillEllipse(badgeBrush, 17, 3, 12, 12);
                using var badgePen = new Pen(Color.White, 1.6f);
                g.DrawLine(badgePen, 23, 12, 23, 6);
                g.DrawLine(badgePen, 20, 9, 23, 6);
                g.DrawLine(badgePen, 26, 9, 23, 6);
            }
        }

        var hIcon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static void FillRoundedRectangle(Graphics g, Brush brush, float x, float y, float width, float height, float radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(x, y, radius, radius, 180, 90);
        path.AddArc(x + width - radius, y, radius, radius, 270, 90);
        path.AddArc(x + width - radius, y + height - radius, radius, radius, 0, 90);
        path.AddArc(x, y + height - radius, radius, radius, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}

internal static class NativeMethods
{
    internal const int WM_CLIPBOARDUPDATE = 0x031D;
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;
    internal const uint VK_SHIFT = 0x10;
    internal const uint VK_INSERT = 0x2D;

    internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct
    {
        internal uint VirtualKeyCode;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    internal static bool IsKeyDown(uint key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);
}
