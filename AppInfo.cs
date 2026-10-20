using AlexLV.WindowsUi;

/// <summary>Names and identity shared by the tray and every window.</summary>
internal static class AppInfo
{
    // The name people read. Identifiers (exe, mutex, data folder, listener window) stay one word.
    internal const string DisplayName = "Clipped Image to Path";

    internal const string RepositoryUrl = "https://github.com/alexlvcom/ClippedImageToPath";

    // The green of the app icon, lifted for dark mode; text on the accent keeps 4.5:1.
    internal static readonly AccentColors Accent = AccentColors.FromHex("#007850", "#5FD3A0", onDark: "#0B1F16");
}
