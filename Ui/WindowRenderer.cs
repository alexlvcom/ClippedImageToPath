using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AlexLV.WindowsUi;

namespace ClippedImageToPath.Ui;

/// <summary>
/// Developer check, like --make-ico: <c>ClippedImageToPath.exe --render-windows &lt;folder&gt;</c> builds
/// every window off screen in the light and dark themes, lays it out, saves a PNG of each Settings tab
/// and dialog, and writes render.txt with any tab whose content would need vertical scrolling. It never
/// saves settings and runs beside the tray instance.
/// </summary>
internal static class WindowRenderer
{
    internal static int Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new List<string>();
        try
        {
            using var icon = TrayIconFactory.CreateNormal();
            ThemeManager.Initialize(AppInfo.DisplayName, AppInfo.Accent, AppTheme.Light);
            ThemeManager.WindowIcon = WinFormsHosting.ToImageSource(icon);
            var settings = AppSettings.Load();

            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                ThemeManager.Apply(theme);
                var name = theme.ToString().ToLowerInvariant();
                RenderSettings(settings, folder, name, report);
                // Closing Settings without Save restores the saved theme; preview this one again.
                ThemeManager.Apply(theme);

                var servers = new RemoteServersWindow(settings, (_, _) => "preview only");
                Render(servers, Path.Combine(folder, $"remote-servers-{name}.png"), report);

                var list = settings.RemoteServers.Select(s => s.Clone()).ToList();
                var server = list.FirstOrDefault() ?? new RemoteServer { Name = "Server 1", Host = "example.com", User = "user" };
                Render(new ServerEditorWindow(server, list, isNew: false), Path.Combine(folder, $"server-editor-{name}.png"), report);
                Render(new ServerEditorWindow(new RemoteServer { Name = "Server 2", Port = 22 }, list, isNew: true),
                    Path.Combine(folder, $"server-editor-new-{name}.png"), report);

                using var updates = new UpdateService();
                var update = new AppUpdate(new Version(9, 9, 9),
                    new Uri("https://github.com/alexlvcom/ClippedImageToPath/releases/download/v9.9.9/ClippedImageToPath-Setup.exe"),
                    null, new string('0', 64));
                Render(new UpdateWindow(update, updates, CancellationToken.None, _ => { }), Path.Combine(folder, $"update-{name}.png"), report);
            }

            report.Add("OK");
            return 0;
        }
        catch (Exception ex)
        {
            report.Add($"FAILED: {ex}");
            return 1;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(folder, "render.txt"), report);
        }
    }

    private static void RenderSettings(AppSettings settings, string folder, string theme, List<string> report)
    {
        var host = new SettingsHost(
            SmartPasteAvailable: true,
            FileNameExample: $"clipboard_{DateTime.Now:yyyy-MM-dd_HH-mm-ss_fff}.png",
            KeepLatest: 500,
            LogFilePath: Path.Combine(settings.OutputDirectory, "bridge.log"),
            Running: () => settings,
            Save: _ => "preview only",
            CheckForUpdates: () => Task.FromResult(new UpdateStatus(UpdateState.UpToDate, "Preview", "Preview")),
            ManageServers: _ => { });
        var window = new SettingsWindow(settings.Clone(), host);
        Show(window);
        try
        {
            foreach (TabItem tab in window.Tabs.Items)
            {
                window.Tabs.SelectedItem = tab;
                Pump();
                var header = (string)tab.Header;
                CheckFits(window, header, report);
                Save(window, Path.Combine(folder, $"settings-{header.ToLowerInvariant().Replace(' ', '-')}-{theme}.png"));
                report.Add($"settings/{header} ({theme}) rendered");
            }
        }
        finally
        {
            window.Close();
        }
    }

    // A tab's content must fit the fixed window: measure it unconstrained in height and compare.
    private static void CheckFits(SettingsWindow window, string header, List<string> report)
    {
        if (window.Tabs.Template.FindName("PART_SelectedContentHost", window.Tabs) is not ContentPresenter host
            || VisualTreeHelper.GetChildrenCount(host) == 0
            || VisualTreeHelper.GetChild(host, 0) is not FrameworkElement content)
        {
            report.Add($"settings/{header}: content not found");
            return;
        }

        content.Measure(new Size(host.ActualWidth, double.PositiveInfinity));
        var needed = content.DesiredSize.Height;
        if (needed > host.ActualHeight + 0.5) report.Add($"SCROLL settings/{header}: needs {needed:0}px, has {host.ActualHeight:0}px");
        else report.Add($"fits settings/{header}: {needed:0} of {host.ActualHeight:0}px");
        content.InvalidateMeasure();
    }

    private static void Render(Window window, string path, List<string> report)
    {
        Show(window);
        try
        {
            Save(window, path);
            report.Add($"{Path.GetFileNameWithoutExtension(path)} rendered ({window.ActualWidth:0}x{window.ActualHeight:0})");
        }
        finally
        {
            window.Close();
        }
    }

    private static void Show(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        Pump();
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);
        // A VisualBrush stretches the content's drawn bounds; pin it to the full laid-out box instead.
        content.Arrange(new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle((Brush)window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, width, height) }, null, new Rect(0, 0, width, height));
        }

        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // Let layout, bindings and the first render pass finish before measuring or capturing.
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
