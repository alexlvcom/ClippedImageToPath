using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AlexLV.WindowsUi;

namespace ClippedImageToPath.Ui;

internal enum UpdateState
{
    NotChecked,
    Checking,
    UpToDate,
    Available,
    Failed,
}

/// <summary>What the Updates tab's status card shows after a check.</summary>
internal sealed record UpdateStatus(UpdateState State, string Title, string Detail);

/// <summary>What Settings needs from the tray app that owns the running settings.</summary>
internal sealed record SettingsHost(
    bool SmartPasteAvailable,
    string FileNameExample,
    int KeepLatest,
    string LogFilePath,
    Func<AppSettings> Running,
    Func<AppSettings, string?> Save,
    Func<Task<UpdateStatus>> CheckForUpdates,
    Action<Window> ManageServers);

/// <summary>
/// Settings: Saved images, Clipboard, Remote upload, Updates, Appearance, About and Diagnostics over
/// one draft copy. Nothing is stored until Save; Cancel, Esc or the title-bar X discards the draft. The
/// theme is the one live preview: it applies at once and is restored on any close without a Save.
/// Remote servers are edited in their own window, which commits with its own Save.
/// </summary>
internal partial class SettingsWindow : ThemedWindow
{
    private static readonly UpdateCheckFrequency[] Frequencies =
    {
        UpdateCheckFrequency.Hourly, UpdateCheckFrequency.Daily, UpdateCheckFrequency.Weekly,
        UpdateCheckFrequency.Monthly, UpdateCheckFrequency.Never,
    };

    private readonly AppSettings _draft;
    private readonly AppSettings _original;
    private readonly SettingsHost _host;
    private bool _saved;
    private bool _loading = true;

    internal SettingsWindow(AppSettings draft, SettingsHost host)
    {
        InitializeComponent();
        _draft = draft;
        _original = draft.Clone();
        _host = host;

        Title = $"{AppInfo.DisplayName} Settings (v{Changelog.MarketingVersion})";

        Folder.Text = draft.OutputDirectory;
        FileNameExample.Text = host.FileNameExample;
        CleanupText.Text = $"Keeps the newest {host.KeepLatest}";
        Wsl.IsChecked = draft.ConvertToWslPath;
        SmartPaste.IsChecked = draft.SmartPasteEnabled;
        if (!host.SmartPasteAvailable)
        {
            SmartPaste.IsEnabled = false;
            SmartPasteLabel.Opacity = 0.45;
            SmartPasteDetail.Text = "Unavailable: the Shift+Insert keyboard hook could not start";
        }

        Upload.IsChecked = draft.RemoteUploadEnabled;
        Remind.IsChecked = draft.RemoteUploadReminderEnabled;
        Minutes.Text = draft.RemoteUploadReminderMinutes.ToString();
        Frequency.SelectedIndex = Math.Max(0, Array.IndexOf(Frequencies, UpdateSchedule.Normalize(draft.UpdateCheckFrequency)));
        (draft.Theme switch { AppTheme.Light => ThemeLight, AppTheme.Dark => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        RefreshServers();
        RefreshThemeHelp();

        foreach (var box in new[] { Wsl, SmartPaste, Upload, Remind })
        {
            box.Checked += (_, _) => OnEdited();
            box.Unchecked += (_, _) => OnEdited();
        }
        Folder.TextChanged += (_, _) => OnEdited();
        Minutes.TextChanged += (_, _) => OnEdited();
        Frequency.SelectionChanged += (_, _) => OnEdited();

        ShowUpdateStatus(draft.LastUpdateCheckUtc is { } checkedAt
            ? new UpdateStatus(UpdateState.UpToDate, UpdateMessages.Latest(Changelog.MarketingVersion),
                UpdateMessages.LastChecked(checkedAt.ToLocalTime()))
            : new UpdateStatus(UpdateState.NotChecked, "Not checked yet",
                $"Version {Changelog.MarketingVersion} · Use Check for updates to look now"));

        BuildAbout();
        BuildDiagnostics();
        _loading = false;
        OnEdited();

        ThemeManager.ThemeChanged += OnThemeChanged;
        Loaded += (_, _) => SaveButton.Focus();
        Closing += OnClosing;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    internal void SelectTab(string name)
    {
        foreach (TabItem tab in Tabs.Items)
        {
            if (string.Equals(tab.Header as string, name, StringComparison.OrdinalIgnoreCase)) Tabs.SelectedItem = tab;
        }
    }

    private AppTheme SelectedTheme =>
        ThemeLight.IsChecked == true ? AppTheme.Light : ThemeDark.IsChecked == true ? AppTheme.Dark : AppTheme.System;

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ThemeManager.Apply(SelectedTheme);
        OnEdited();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshThemeHelp();

    private void RefreshThemeHelp() =>
        ThemeHelp.Text = $"System follows the Windows app mode. Windows is using {(ThemeManager.SystemUsesLightTheme() ? "Light" : "Dark")} right now.";

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border row) return;
        var toggle = FindToggle(row);
        if (toggle is null || !toggle.IsEnabled) return;
        if (e.OriginalSource is DependencyObject source && IsInside(source, toggle)) return;
        toggle.IsChecked = toggle.IsChecked != true;
        toggle.Focus();
    }

    private static CheckBox? FindToggle(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is CheckBox box) return box;
            if (FindToggle(child) is { } nested) return nested;
        }

        return null;
    }

    private static bool IsInside(DependencyObject node, DependencyObject ancestor)
    {
        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == ancestor) return true;
        }

        return false;
    }

    /// <summary>Copies the controls into the draft and refreshes dependent state and the change count.</summary>
    private void OnEdited()
    {
        if (_loading) return;

        var uploadOn = Upload.IsChecked == true;
        Remind.IsEnabled = uploadOn;
        RemindLabel.Opacity = uploadOn ? 1 : 0.45;
        var minutesOn = uploadOn && Remind.IsChecked == true;
        Minutes.IsEnabled = minutesOn;
        EveryLabel.Opacity = MinLabel.Opacity = minutesOn ? 1 : 0.45;

        _draft.OutputDirectory = Folder.Text.Trim();
        _draft.ConvertToWslPath = Wsl.IsChecked == true;
        _draft.SmartPasteEnabled = SmartPaste.IsChecked == true;
        _draft.RemoteUploadEnabled = uploadOn;
        _draft.RemoteUploadReminderEnabled = Remind.IsChecked == true;
        if (TryReadMinutes(out var minutes)) _draft.RemoteUploadReminderMinutes = minutes;
        _draft.UpdateCheckFrequency = Frequencies[Math.Max(0, Frequency.SelectedIndex)];
        _draft.Theme = SelectedTheme;
        if (FolderError.Visibility == Visibility.Visible && _draft.OutputDirectory.Length > 0) ShowError(Folder, FolderError, false);
        if (MinutesError.Visibility == Visibility.Visible && TryReadMinutes(out _)) ShowError(Minutes, MinutesError, false);
        UploadDetail.Text = DescribeUpload();

        var changes = 0;
        if (!string.Equals(_draft.OutputDirectory, _original.OutputDirectory, StringComparison.OrdinalIgnoreCase)) changes++;
        if (_draft.ConvertToWslPath != _original.ConvertToWslPath) changes++;
        if (_draft.SmartPasteEnabled != _original.SmartPasteEnabled) changes++;
        if (_draft.RemoteUploadEnabled != _original.RemoteUploadEnabled) changes++;
        if (_draft.RemoteUploadReminderEnabled != _original.RemoteUploadReminderEnabled) changes++;
        if (Minutes.Text.Trim() != _original.RemoteUploadReminderMinutes.ToString()) changes++;
        if (_draft.UpdateCheckFrequency != UpdateSchedule.Normalize(_original.UpdateCheckFrequency)) changes++;
        if (_draft.Theme != _original.Theme) changes++;
        DirtyText.Text = changes switch { 0 => "", 1 => "1 unsaved change", _ => $"{changes} unsaved changes" };
    }

    private string DescribeUpload()
    {
        var active = _host.Running().GetActiveServer();
        if (active is null) return "No server yet: add one with Manage servers…";
        return Upload.IsChecked == true ? $"Uploads each saved image to {active.Name}" : $"Off · would upload to {active.Name}";
    }

    private void RefreshServers()
    {
        var running = _host.Running();
        var count = running.RemoteServers.Count;
        ServersDetail.Text = count switch
        {
            0 => "No servers configured",
            1 => $"1 server · active: {running.GetActiveServer()?.Name}",
            _ => $"{count} servers · active: {running.GetActiveServer()?.Name}",
        };
        // The manager commits servers itself; keep the draft in step so nothing stale is shown.
        _draft.RemoteServers = running.RemoteServers.Select(s => s.Clone()).ToList();
        _draft.ActiveServer = running.ActiveServer;
        UploadDetail.Text = DescribeUpload();
    }

    private bool TryReadMinutes(out int minutes) =>
        int.TryParse(Minutes.Text.Trim(), out minutes)
        && minutes is >= AppSettings.MinRemoteUploadReminderMinutes and <= AppSettings.MaxRemoteUploadReminderMinutes;

    private static void ShowError(TextBox box, TextBlock message, bool show)
    {
        message.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) box.SetResourceReference(StyleProperty, "InvalidTextBox");
        else box.ClearValue(StyleProperty);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        OnEdited();
        if (_draft.OutputDirectory.Length == 0)
        {
            Tabs.SelectedItem = SavedTab;
            ShowError(Folder, FolderError, true);
            Folder.Focus();
            return;
        }

        // The interval only matters while reminders are on, but a bad value is never saved.
        if (!TryReadMinutes(out _))
        {
            Tabs.SelectedItem = RemoteTab;
            ShowError(Minutes, MinutesError, true);
            Minutes.Focus();
            Minutes.SelectAll();
            return;
        }

        var error = _host.Save(_draft);
        if (error is not null)
        {
            MessageDialog.Show($"Could not save settings. Your changes are still open in this window.\n\n{error}",
                MessageKind.Error, owner: this);
            return;
        }

        _saved = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Any close without a successful Save restores the saved theme.
        if (!_saved && ThemeManager.Choice != _original.Theme) ThemeManager.Apply(_original.Theme);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where clipboard images will be saved",
        };
        try
        {
            var current = Folder.Text.Trim();
            if (current.Length > 0 && Directory.Exists(current)) picker.InitialDirectory = current;
        }
        catch (ArgumentException)
        {
            // An invalid typed path: open the picker at its default location.
        }

        if (picker.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(picker.FolderName)) Folder.Text = picker.FolderName;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedPath = Path.GetFullPath(Folder.Text.Trim());
            if (!Directory.Exists(selectedPath))
                throw new DirectoryNotFoundException("This folder does not exist. Save settings to create it first.");
            Process.Start(new ProcessStartInfo { FileName = selectedPath, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException
                                      or UnauthorizedAccessException or Win32Exception or System.Security.SecurityException)
        {
            MessageDialog.Show($"Could not open the output folder.\n\n{ex.Message}", MessageKind.Warning, owner: this);
        }
    }

    private void ManageServers_Click(object sender, RoutedEventArgs e)
    {
        _host.ManageServers(this);
        RefreshServers();
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        ShowUpdateStatus(new UpdateStatus(UpdateState.Checking, "Checking for updates…", "Contacting GitHub releases"));
        try
        {
            ShowUpdateStatus(await _host.CheckForUpdates());
        }
        catch (Exception ex)
        {
            // The card must never stay on "Checking…".
            ShowUpdateStatus(new UpdateStatus(UpdateState.Failed, "Could not check for updates",
                $"Version {Changelog.MarketingVersion} · {ex.Message}"));
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private void ShowUpdateStatus(UpdateStatus status)
    {
        UpdateTitle.Text = status.Title;
        UpdateDetail.Text = status.Detail;
        UpdateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, status.State switch
        {
            UpdateState.Failed => "Danger",
            UpdateState.Available or UpdateState.Checking => "Accent",
            UpdateState.NotChecked => "Muted",
            _ => "Ok",
        });
    }

    private static DateTime BuildDate
    {
        get
        {
            try
            {
                var path = Environment.ProcessPath;
                return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.MinValue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return DateTime.MinValue;
            }
        }
    }

    private void BuildAbout()
    {
        AboutIcon.Source = ThemeManager.WindowIcon;
        AboutTitle.Text = AppInfo.DisplayName;
        var built = BuildDate == DateTime.MinValue ? "" : $" · Built {BuildDate:yyyy-MM-dd HH:mm}";
        AboutBuild.Text = $"Version {Changelog.MarketingVersion}{built} · © {DateTime.Now.Year} Alex LV";
        NotesHeader.Text = $"Release notes for v{Changelog.MarketingVersion}:";
        ReleaseNotesView.Fill(NotesPanel, Changelog.Current);
    }

    private void BuildDiagnostics()
    {
        var exe = Environment.ProcessPath ?? "";
        var rows = new (string Label, string Path, string Folder)[]
        {
            ("Executable", exe, Path.GetDirectoryName(exe) ?? ""),
            ("Settings", AppSettings.SettingsFilePath, AppSettings.SettingsFolder),
            ("Log", _host.LogFilePath, Path.GetDirectoryName(_host.LogFilePath) ?? ""),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var (label, path, folder) = rows[i];
            var row = new Border();
            row.SetResourceReference(StyleProperty, i == 0 ? "FirstCardRow" : "CardRow");
            var stack = new StackPanel();
            var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) };
            caption.SetResourceReference(StyleProperty, "MutedText");
            stack.Children.Add(caption);

            var line = new DockPanel();
            var open = new Button { Content = "Open", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            open.Click += (_, _) => OpenFolder(folder, this);
            DockPanel.SetDock(open, Dock.Right);
            line.Children.Add(open);
            var box = new TextBox { Text = path };
            box.SetResourceReference(StyleProperty, "PathBox");
            System.Windows.Automation.AutomationProperties.SetName(box, label);
            line.Children.Add(box);
            stack.Children.Add(line);
            row.Child = stack;
            PathsPanel.Children.Add(row);
        }
    }

    private void ProjectPage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = AppInfo.RepositoryUrl, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageDialog.Show($"Could not open {AppInfo.RepositoryUrl}.\n\n{ex.Message}", MessageKind.Warning, owner: this);
        }
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        var built = BuildDate == DateTime.MinValue ? "Unknown" : BuildDate.ToString("yyyy-MM-dd HH:mm");
        var details = $"{AppInfo.DisplayName} v{Changelog.MarketingVersion}\n" +
                      $"Built: {built}\n" +
                      $"Executable: {Environment.ProcessPath}\n" +
                      $"Settings: {AppSettings.SettingsFilePath}\n" +
                      $"Log: {_host.LogFilePath}\n" +
                      $"Theme: {ThemeManager.Choice} ({(ThemeManager.IsDark ? "dark" : "light")})\n" +
                      $"Repository: {AppInfo.RepositoryUrl}\n\n" +
                      $"Release notes (v{Changelog.MarketingVersion}):\n{Changelog.GetReleaseNotes(Changelog.MarketingVersion)}";
        try
        {
            Clipboard.SetText(details);
            MessageDialog.Show("Application details copied to clipboard.", MessageKind.Success, owner: this);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            MessageDialog.Show($"Could not copy to the clipboard.\n\n{ex.Message}", MessageKind.Warning, owner: this);
        }
    }

    internal static void OpenFolder(string path, Window? owner)
    {
        try
        {
            if (File.Exists(path)) path = Path.GetDirectoryName(path) ?? path;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                                      or InvalidOperationException or ArgumentException)
        {
            MessageDialog.Show($"Failed to open folder: {ex.Message}", MessageKind.Error, owner: owner);
        }
    }
}
