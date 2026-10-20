/// <summary>
/// Update-check wording shared by the Updates status card and any message. Results always name the
/// running version; an up-to-date result never talks about installers.
/// </summary>
internal static class UpdateMessages
{
    internal static string Latest(string version) => $"You're running the latest version (v{version})";

    internal static string LastChecked(DateTime checkedAtLocal) => $"Last checked {checkedAtLocal:yyyy-MM-dd HH:mm}";
}
