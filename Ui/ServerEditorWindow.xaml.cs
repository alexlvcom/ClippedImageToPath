using System.Windows;
using System.Windows.Controls;
using AlexLV.WindowsUi;

namespace ClippedImageToPath.Ui;

/// <summary>
/// Adds or edits one remote server in place. Field errors show under their field; the server is only
/// changed when Save finds every value valid.
/// </summary>
internal partial class ServerEditorWindow : ThemedWindow
{
    private static readonly RemoteProtocol[] ProtocolOrder =
    {
        RemoteProtocol.Sftp, RemoteProtocol.Ftp, RemoteProtocol.FtpsExplicit, RemoteProtocol.FtpsImplicit,
    };

    private readonly RemoteServer _server;
    private readonly List<RemoteServer> _servers;
    private RemoteProtocol _lastProtocol;
    private bool _loading = true;

    internal ServerEditorWindow(RemoteServer server, List<RemoteServer> servers, bool isNew)
    {
        InitializeComponent();
        _server = server;
        _servers = servers;
        Title = isNew ? "Add Remote Server" : "Edit Remote Server";

        NameBox.Text = server.Name;
        _lastProtocol = server.GetProtocol();
        ProtocolBox.SelectedIndex = Math.Max(0, Array.IndexOf(ProtocolOrder, _lastProtocol));
        HostBox.Text = server.Host;
        PortBox.Text = server.Port.ToString();
        UserBox.Text = server.User;
        PasswordBox.Password = server.GetPassword();
        DirectoryBox.Text = server.RemoteDirectory;
        PassiveBox.IsChecked = server.PassiveMode;
        _loading = false;
        UpdatePassive();

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    internal static int DefaultPortFor(RemoteProtocol protocol) => protocol switch
    {
        RemoteProtocol.Ftp => 21,
        RemoteProtocol.FtpsExplicit => 21,
        RemoteProtocol.FtpsImplicit => 990,
        _ => 22,
    };

    private RemoteProtocol SelectedProtocol => ProtocolOrder[Math.Max(0, ProtocolBox.SelectedIndex)];

    private void Protocol_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        // Follow the new protocol's conventional port while the field still holds the old default.
        var protocol = SelectedProtocol;
        if (int.TryParse(PortBox.Text.Trim(), out var port) && port == DefaultPortFor(_lastProtocol))
        {
            PortBox.Text = DefaultPortFor(protocol).ToString();
        }

        _lastProtocol = protocol;
        UpdatePassive();
    }

    // Passive mode only applies to FTP and FTPS.
    private void UpdatePassive()
    {
        var applies = SelectedProtocol != RemoteProtocol.Sftp;
        PassiveBox.IsEnabled = applies;
        PassiveLabel.Opacity = applies ? 1 : 0.45;
    }

    private static void SetError(Control field, TextBlock message, string? error)
    {
        message.Text = error ?? "";
        message.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is null) field.ClearValue(BorderBrushProperty);
        else field.SetResourceReference(BorderBrushProperty, "Danger");
    }

    /// <summary>Checks every field, shows each error under its field, and focuses the first one.</summary>
    private bool TryRead(bool checkName, out RemoteUploadConfig config)
    {
        config = default;
        var name = NameBox.Text.Trim();
        var host = HostBox.Text.Trim();
        var user = UserBox.Text.Trim();
        var password = PasswordBox.Password;
        var directory = DirectoryBox.Text.Trim();
        var portValid = int.TryParse(PortBox.Text.Trim(), out var port) && port is > 0 and <= 65535;

        string? nameError = null;
        if (checkName && name.Length == 0) nameError = "Name cannot be empty.";
        else if (checkName && _servers.Any(s => !ReferenceEquals(s, _server) && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            nameError = $"A server named '{name}' already exists.";

        var checks = new (Control Field, TextBlock Message, string? Error)[]
        {
            (NameBox, NameError, nameError),
            (HostBox, HostError, host.Length == 0 ? "Host cannot be empty." : null),
            (PortBox, PortError, portValid ? null : "Port must be a number from 1 to 65535."),
            (UserBox, UserError, user.Length == 0 ? "User cannot be empty." : null),
            (PasswordBox, PasswordError, string.IsNullOrWhiteSpace(password) ? "Password cannot be empty." : null),
            (DirectoryBox, DirectoryError, directory.Length == 0 ? "Remote directory cannot be empty." : null),
        };

        Control? first = null;
        foreach (var (field, message, error) in checks)
        {
            SetError(field, message, error);
            if (error is not null) first ??= field;
        }

        if (first is not null)
        {
            first.Focus();
            return false;
        }

        config = new RemoteUploadConfig(SelectedProtocol, host, port, user, password, directory, PassiveBox.IsChecked == true);
        return true;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRead(checkName: false, out var config)) return;

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
            TestButton.Content = "Test connection";
            TestButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryRead(checkName: true, out var config)) return;

        _server.Name = NameBox.Text.Trim();
        _server.SetProtocol(config.Protocol);
        _server.PassiveMode = config.PassiveMode;
        _server.Host = config.Host;
        _server.Port = config.Port;
        _server.User = config.User;
        _server.SetPassword(config.Password);
        _server.RemoteDirectory = config.RemoteDirectory;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
