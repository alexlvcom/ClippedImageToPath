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

/// <summary>
/// Update-check wording shared by the Updates status card and any message. Results always name the
/// running version; an up-to-date result never talks about installers.
/// </summary>
internal static class UpdateMessages
{
    internal static string Latest(string version) => $"You're running the latest version (v{version})";

    internal static string Available(string version) => $"Version {version} is available";

    internal static string LastChecked(DateTime checkedAtLocal) => $"Last checked {checkedAtLocal:yyyy-MM-dd HH:mm}";

    /// <summary>
    /// The card shown when Settings opens: the remembered outcome of the last check, never a guess.
    /// A remembered newer version that is no longer newer (the app was updated since) counts as not checked.
    /// </summary>
    internal static UpdateStatus FromLastCheck(AppSettings settings, string current)
    {
        var notChecked = new UpdateStatus(UpdateState.NotChecked, "Not checked yet",
            $"Version {current} · Use Check for updates to look now");
        if (settings.LastUpdateCheckUtc is not { } checkedAtUtc) return notChecked;
        var checkedAt = LastChecked(checkedAtUtc.ToLocalTime());

        switch (settings.LastUpdateOutcome)
        {
            case UpdateCheckOutcome.Current when settings.LastUpdateCheckVersion == current:
                return new UpdateStatus(UpdateState.UpToDate, Latest(current), checkedAt);
            case UpdateCheckOutcome.Available
                when Version.TryParse(settings.LastAvailableVersion, out var newer)
                     && Version.TryParse(current, out var running) && newer > running:
                return new UpdateStatus(UpdateState.Available, Available(settings.LastAvailableVersion!),
                    $"You have {current} · {checkedAt}");
            case UpdateCheckOutcome.Failed when settings.LastUpdateCheckVersion == current:
                return new UpdateStatus(UpdateState.Failed, "Could not check for updates",
                    $"Version {current} · {settings.LastUpdateError} · {checkedAt}");
            default:
                return notChecked;
        }
    }
}
