using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlexLV.WindowsUi;

/// <summary>What the last update check found. Saved by number; keep existing values stable.</summary>
internal enum UpdateCheckOutcome
{
    None = 0,
    Current = 1,
    Available = 2,
    Failed = 3,
}

internal sealed class AppSettings
{
    internal const int DefaultRemoteUploadReminderMinutes = 30;
    internal const int MinRemoteUploadReminderMinutes = 1;
    internal const int MaxRemoteUploadReminderMinutes = 24 * 60;

    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "ClipboardImages");

    public UpdateCheckFrequency UpdateCheckFrequency { get; set; } = UpdateCheckFrequency.Daily;

    public bool ConvertToWslPath { get; set; }

    public bool SmartPasteEnabled { get; set; } = true;

    public bool RemoteUploadEnabled { get; set; }

    public bool RemoteUploadReminderEnabled { get; set; } = true;

    public int RemoteUploadReminderMinutes { get; set; } = DefaultRemoteUploadReminderMinutes;

    // Appearance → Theme. System follows the Windows app mode.
    public AppTheme Theme { get; set; } = AppTheme.System;

    // Last automatic or manual update check and its outcome, shown on the Updates tab when Settings opens.
    public DateTime? LastUpdateCheckUtc { get; set; }

    public UpdateCheckOutcome LastUpdateOutcome { get; set; }

    // The running version when that check ran, so a result from before an update is not shown as current.
    public string? LastUpdateCheckVersion { get; set; }

    public string? LastAvailableVersion { get; set; }

    public string? LastUpdateError { get; set; }

    // Named remote server profiles. The one whose Name matches ActiveServer is used for uploads.
    public List<RemoteServer> RemoteServers { get; set; } = new();

    // Name of the currently selected server profile.
    public string ActiveServer { get; set; } = string.Empty;

    // --- Legacy single-server fields (nullable, read only for one-time migration into RemoteServers).
    // They are set to null after migration and omitted from the saved file.
    public string? Protocol { get; set; }

    public bool? PassiveMode { get; set; }

    public string? SshHost { get; set; }

    public int? SshPort { get; set; }

    public string? SshUser { get; set; }

    public string? SshPasswordBase64 { get; set; }

    public string? RemoteDirectory { get; set; }

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClippedImageToPath");

    internal static string SettingsFolder => SettingsDirectory;

    internal static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    // Where Save writes and Load reads; tests point it at a temporary file.
    private string _filePath = SettingsFilePath;

    internal static AppSettings Load() => Load(SettingsFilePath);

    internal static AppSettings Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new AppSettings { _filePath = filePath };
            }

            var json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<AppSettings>(json);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.OutputDirectory))
            {
                return new AppSettings { _filePath = filePath };
            }

            parsed._filePath = filePath;
            parsed.OutputDirectory = Path.GetFullPath(parsed.OutputDirectory);
            parsed.Theme = ThemeManager.Normalize(parsed.Theme);
            parsed.RemoteServers ??= new List<RemoteServer>();
            parsed.NormalizeRemoteUploadReminder();
            parsed.MigrateLegacyServer();
            parsed.NormalizeServers();
            var passwordsUpgraded = parsed.UpgradePasswordStorage();

            if (parsed.RemoteServers.Count > 0 &&
                !parsed.RemoteServers.Any(s => string.Equals(s.Name, parsed.ActiveServer, StringComparison.Ordinal)))
            {
                parsed.ActiveServer = parsed.RemoteServers[0].Name;
            }

            if (passwordsUpgraded)
            {
                // Re-encrypt legacy plaintext-base64 passwords with DPAPI and drop the old field.
                try
                {
                    parsed.Save();
                }
                catch
                {
                    // Best-effort upgrade; GetPassword still reads the in-memory value this session.
                }
            }

            return parsed;
        }
        catch
        {
            return new AppSettings { _filePath = filePath };
        }
    }

    // A deep copy for a Settings draft, so Cancel leaves the running settings untouched.
    internal AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.RemoteServers = RemoteServers.Select(s => s.Clone()).ToList();
        return copy;
    }

    private void NormalizeRemoteUploadReminder()
    {
        if (RemoteUploadReminderMinutes < MinRemoteUploadReminderMinutes ||
            RemoteUploadReminderMinutes > MaxRemoteUploadReminderMinutes)
        {
            RemoteUploadReminderMinutes = DefaultRemoteUploadReminderMinutes;
        }
    }

    // Fold a pre-1.3 single-server configuration into the RemoteServers list.
    private void MigrateLegacyServer()
    {
        if (RemoteServers.Count == 0 && !string.IsNullOrWhiteSpace(SshHost))
        {
            var migrated = new RemoteServer
            {
                Name = SshHost!,
                Protocol = string.IsNullOrWhiteSpace(Protocol) ? "Sftp" : Protocol!,
                PassiveMode = PassiveMode ?? true,
                Host = SshHost!,
                Port = (SshPort is > 0 and <= 65535) ? SshPort.Value : 22,
                User = SshUser ?? string.Empty,
                PasswordBase64 = SshPasswordBase64 ?? string.Empty,
                RemoteDirectory = string.IsNullOrWhiteSpace(RemoteDirectory) ? "." : RemoteDirectory!,
            };
            RemoteServers.Add(migrated);
            if (string.IsNullOrWhiteSpace(ActiveServer))
            {
                ActiveServer = migrated.Name;
            }
        }

        // Drop legacy fields so they no longer serialize.
        Protocol = null;
        PassiveMode = null;
        SshHost = null;
        SshPort = null;
        SshUser = null;
        SshPasswordBase64 = null;
        RemoteDirectory = null;
    }

    private void NormalizeServers()
    {
        foreach (var server in RemoteServers)
        {
            server.Name ??= string.Empty;
            if (server.Port <= 0 || server.Port > 65535)
            {
                server.Port = 22;
            }

            if (string.IsNullOrWhiteSpace(server.RemoteDirectory))
            {
                server.RemoteDirectory = ".";
            }

            if (string.IsNullOrWhiteSpace(server.Protocol))
            {
                server.Protocol = "Sftp";
            }
        }
    }

    // Re-encrypt any legacy plaintext-base64 passwords with DPAPI.
    // Returns true if any server's stored password representation changed.
    private bool UpgradePasswordStorage()
    {
        var changed = false;
        foreach (var server in RemoteServers)
        {
            if (server.UpgradePasswordStorage())
            {
                changed = true;
            }
        }

        return changed;
    }

    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        var json = JsonSerializer.Serialize(this, options);
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _filePath, overwrite: true);
    }

    internal RemoteServer? GetActiveServer()
    {
        if (RemoteServers.Count == 0)
        {
            return null;
        }

        return RemoteServers.FirstOrDefault(s => string.Equals(s.Name, ActiveServer, StringComparison.Ordinal))
            ?? RemoteServers[0];
    }

    internal bool TryGetRemoteUploadConfig(out RemoteUploadConfig config, out string error)
    {
        config = default;
        error = string.Empty;

        var server = GetActiveServer();
        if (server is null)
        {
            error = "no remote server is configured";
            return false;
        }

        return server.TryGetConfig(out config, out error);
    }
}

internal sealed class RemoteServer
{
    public string Name { get; set; } = string.Empty;

    // One of: "Sftp", "Ftp", "FtpsExplicit", "FtpsImplicit".
    public string Protocol { get; set; } = "Sftp";

    public bool PassiveMode { get; set; } = true;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string User { get; set; } = string.Empty;

    // DPAPI-encrypted password (Windows CurrentUser scope), base64-encoded and tagged with a
    // scheme prefix. Only decryptable by the same Windows user account that saved it.
    public string Password { get; set; } = string.Empty;

    // Legacy (<= 1.3.3): password stored as plain base64 (encoding, not encryption).
    // Read for backward compatibility, then migrated into Password and dropped on save.
    public string? PasswordBase64 { get; set; }

    public string RemoteDirectory { get; set; } = ".";

    private const string DpapiScheme = "DPAPI:";

    internal RemoteServer Clone() => (RemoteServer)MemberwiseClone();

    internal string GetPassword()
    {
        if (!string.IsNullOrWhiteSpace(Password))
        {
            return DecodeStoredPassword(Password);
        }

        // Legacy plaintext-base64 fallback (pre-1.3.4 settings not yet migrated).
        if (!string.IsNullOrWhiteSpace(PasswordBase64))
        {
            return DecodeLegacyBase64(PasswordBase64);
        }

        return string.Empty;
    }

    internal void SetPassword(string password)
    {
        Password = EncodePassword(password);
        PasswordBase64 = null;
    }

    // Upgrade a legacy plaintext-base64 password to DPAPI encryption and drop the old field.
    // Returns true if the stored representation changed (so the caller can persist it).
    internal bool UpgradePasswordStorage()
    {
        if (string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(PasswordBase64))
        {
            Password = EncodePassword(DecodeLegacyBase64(PasswordBase64));
        }

        if (PasswordBase64 is null)
        {
            return false;
        }

        // Remove the legacy field so plaintext base64 no longer persists to disk.
        PasswordBase64 = null;
        return true;
    }

    private static string EncodePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return DpapiScheme + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            // DPAPI should always be available on Windows; degrade to base64 if it ever isn't.
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
        }
    }

    private static string DecodeStoredPassword(string stored)
    {
        if (stored.StartsWith(DpapiScheme, StringComparison.Ordinal))
        {
            try
            {
                var blob = Convert.FromBase64String(stored[DpapiScheme.Length..]);
                var bytes = ProtectedData.Unprotect(blob, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // Wrong Windows user, corrupted blob, or a tampered settings file.
                return string.Empty;
            }
        }

        // Untagged value: treat as legacy plaintext base64.
        return DecodeLegacyBase64(stored);
    }

    private static string DecodeLegacyBase64(string base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch
        {
            return string.Empty;
        }
    }

    internal RemoteProtocol GetProtocol()
    {
        return Enum.TryParse<RemoteProtocol>(Protocol, ignoreCase: true, out var parsed)
            ? parsed
            : RemoteProtocol.Sftp;
    }

    internal void SetProtocol(RemoteProtocol protocol)
    {
        Protocol = protocol.ToString();
    }

    internal string DisplayLine()
    {
        var protocolLabel = GetProtocol() switch
        {
            RemoteProtocol.Sftp => "SFTP",
            RemoteProtocol.Ftp => "FTP",
            RemoteProtocol.FtpsExplicit => "FTPS",
            RemoteProtocol.FtpsImplicit => "FTPS(implicit)",
            _ => "SFTP",
        };
        return $"{Name}  —  {protocolLabel} {User}@{Host}:{Port}";
    }

    internal bool TryGetConfig(out RemoteUploadConfig config, out string error)
    {
        config = default;
        error = string.Empty;

        var password = GetPassword();
        if (string.IsNullOrWhiteSpace(Host))
        {
            error = $"host is not configured for server '{Name}'";
            return false;
        }

        if (Port <= 0 || Port > 65535)
        {
            error = $"port is invalid for server '{Name}'";
            return false;
        }

        if (string.IsNullOrWhiteSpace(User))
        {
            error = $"user is not configured for server '{Name}'";
            return false;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            error = $"password is not configured for server '{Name}'";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RemoteDirectory))
        {
            error = $"remote directory is not configured for server '{Name}'";
            return false;
        }

        config = new RemoteUploadConfig(GetProtocol(), Host, Port, User, password, RemoteDirectory, PassiveMode);
        return true;
    }
}

internal enum RemoteProtocol
{
    Sftp,
    Ftp,
    FtpsExplicit,
    FtpsImplicit,
}

internal readonly record struct RemoteUploadConfig(
    RemoteProtocol Protocol,
    string Host,
    int Port,
    string User,
    string Password,
    string RemoteDirectory,
    bool PassiveMode);
