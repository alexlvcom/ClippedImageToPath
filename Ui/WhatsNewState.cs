using System.IO;

namespace ClippedImageToPath.Ui;

/// <summary>Per-PC release acknowledgement, separate from settings drafts and Drive sync.</summary>
internal sealed class WhatsNewState(string? path = null, string? settingsPath = null)
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClippedImageToPath", "last-seen-release.txt");
    private readonly string _settingsPath = settingsPath ?? AppSettings.SettingsFilePath;

    public bool ShouldShow(string currentVersion)
    {
        if (!Version.TryParse(currentVersion, out var current)) return false;
        try
        {
            return !File.Exists(_path) || !Version.TryParse(File.ReadAllText(_path).Trim(), out var previous) || current > previous;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>The release this PC last acknowledged, or null for a fresh installation.</summary>
    public Version? PreviousRelease()
    {
        try
        {
            if (File.Exists(_path)) return Version.TryParse(File.ReadAllText(_path).Trim(), out var seen) ? seen : null;
            // Releases before 1.8.2 kept no record, so their settings tell an upgrade from a fresh installation.
            // 1.8.0 added the Theme setting; 1.8.1 is the last release before the record.
            if (!File.Exists(_settingsPath)) return null;
            return File.ReadAllText(_settingsPath).Contains("\"Theme\"", StringComparison.Ordinal) ? new Version(1, 8, 1) : new Version(1, 7, 3);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public void MarkShown(string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, version);
        File.Move(temp, _path, overwrite: true);
    }
}
