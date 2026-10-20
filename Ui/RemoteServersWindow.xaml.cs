using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AlexLV.WindowsUi;

namespace ClippedImageToPath.Ui;

/// <summary>
/// Edits a draft copy of the remote server profiles and the active server. Nothing is written until
/// Save; Cancel, Esc or the title-bar X discards every add, edit, removal and active change.
/// </summary>
internal partial class RemoteServersWindow : ThemedWindow
{
    private readonly List<RemoteServer> _servers;
    private readonly Func<List<RemoteServer>, string, string?> _save;
    private readonly string _originalActive;
    private string _active;
    private bool _changed;

    internal RemoteServersWindow(AppSettings running, Func<List<RemoteServer>, string, string?> save)
    {
        InitializeComponent();
        _servers = running.RemoteServers.Select(s => s.Clone()).ToList();
        _active = _originalActive = running.ActiveServer;
        _save = save;
        Reload(_servers.FindIndex(s => IsActive(s)));
        Loaded += (_, _) => List.Focus();
    }

    private bool IsActive(RemoteServer server) => string.Equals(server.Name, _active, StringComparison.Ordinal);

    private RemoteServer? Selected => List.SelectedIndex >= 0 && List.SelectedIndex < _servers.Count ? _servers[List.SelectedIndex] : null;

    private void Reload(int selectIndex)
    {
        List.Items.Clear();
        foreach (var server in _servers) List.Items.Add(new ListBoxItem { Content = Row(server) });
        EmptyState.Visibility = _servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_servers.Count > 0) List.SelectedIndex = Math.Clamp(selectIndex, 0, _servers.Count - 1);
        UpdateButtons();
        DirtyText.Text = _changed || !string.Equals(_active, _originalActive, StringComparison.Ordinal) ? "Unsaved changes" : "";
    }

    private FrameworkElement Row(RemoteServer server)
    {
        var active = IsActive(server);
        var dot = new Ellipse { Width = 10, Height = 10, StrokeThickness = 2, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.StrokeProperty, active ? "Accent" : "Border");
        if (active) dot.SetResourceReference(Shape.FillProperty, "Accent");

        var name = new TextBlock { Text = server.Name, FontWeight = FontWeights.SemiBold };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(name);
        if (active)
        {
            var badge = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BackgroundProperty, "Selection");
            var label = new TextBlock { Text = "Active", FontSize = 11, FontWeight = FontWeights.SemiBold };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            badge.Child = label;
            title.Children.Add(badge);
        }

        var detail = new TextBlock { Text = Describe(server) };
        detail.SetResourceReference(StyleProperty, "MutedText");

        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(detail);
        var row = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(text);
        System.Windows.Automation.AutomationProperties.SetName(row, (active ? "Active: " : "") + server.DisplayLine());
        return row;
    }

    private static string Describe(RemoteServer server)
    {
        var protocol = server.GetProtocol() switch
        {
            RemoteProtocol.Ftp => "FTP",
            RemoteProtocol.FtpsExplicit => "FTPS (explicit TLS)",
            RemoteProtocol.FtpsImplicit => "FTPS (implicit TLS)",
            _ => "SFTP",
        };
        return $"{protocol} · {server.User}@{server.Host}:{server.Port} · {server.RemoteDirectory}";
    }

    private void UpdateButtons()
    {
        var has = Selected is not null;
        EditButton.IsEnabled = RemoveButton.IsEnabled = TestButton.IsEnabled = has;
        ActiveButton.IsEnabled = has && !IsActive(Selected!);
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null) EditSelected();
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && Selected is not null)
        {
            Remove_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var server = new RemoteServer
        {
            Name = SuggestName(),
            Protocol = RemoteProtocol.Sftp.ToString(),
            Port = ServerEditorWindow.DefaultPortFor(RemoteProtocol.Sftp),
        };
        if (new ServerEditorWindow(server, _servers, isNew: true) { Owner = this }.ShowDialog() != true) return;

        _servers.Add(server);
        if (_servers.Count == 1) _active = server.Name;
        _changed = true;
        Reload(_servers.Count - 1);
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void EditSelected()
    {
        var server = Selected;
        if (server is null) return;

        var previousName = server.Name;
        var index = List.SelectedIndex;
        if (new ServerEditorWindow(server, _servers, isNew: false) { Owner = this }.ShowDialog() != true) return;

        // Keep the active pointer aligned if the active server was renamed.
        if (string.Equals(_active, previousName, StringComparison.Ordinal)) _active = server.Name;
        _changed = true;
        Reload(index);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var server = Selected;
        if (server is null) return;
        if (MessageDialog.Show($"Remove server '{server.Name}'?", MessageKind.Question, MessageButtons.YesNo, this, defaultIsNegative: true)
            != MessageResult.Yes) return;

        var index = List.SelectedIndex;
        var wasActive = IsActive(server);
        _servers.Remove(server);
        if (wasActive) _active = _servers.Count > 0 ? _servers[0].Name : string.Empty;
        _changed = true;
        Reload(index);
    }

    private void Active_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } server) return;
        _active = server.Name;
        Reload(List.SelectedIndex);
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } server) return;
        if (!server.TryGetConfig(out var config, out var error))
        {
            MessageDialog.Show($"Cannot test this server: {error}.", MessageKind.Warning, owner: this);
            return;
        }

        TestButton.IsEnabled = false;
        TestButton.Content = "Testing…";
        try
        {
            await Task.Run(() => RemoteUploader.TestConnection(config));
            MessageDialog.Show("Connection succeeded and the remote directory is available.", MessageKind.Success, owner: this);
        }
        catch (Exception ex)
        {
            MessageDialog.Show($"Connection test failed: {ex.Message}", MessageKind.Error, owner: this);
        }
        finally
        {
            TestButton.Content = "Test";
            UpdateButtons();
        }
    }

    private string SuggestName()
    {
        for (var i = 1; ; i++)
        {
            var candidate = $"Server {i}";
            if (!_servers.Any(s => string.Equals(s.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var error = _save(_servers, _active);
        if (error is not null)
        {
            MessageDialog.Show($"Failed to save remote servers: {error}", MessageKind.Error, owner: this);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
