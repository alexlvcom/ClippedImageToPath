using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>A heading group of one release ("Main changes", "Other updates"); untitled when the entry has none.</summary>
internal sealed record ChangelogSection(string? Heading, IReadOnlyList<string> Bullets);

/// <summary>One release from CHANGELOG.md.</summary>
internal sealed record ChangelogEntry(string Version, string Date, IReadOnlyList<ChangelogSection> Sections);

/// <summary>
/// Version and release notes. CHANGELOG.md, embedded at build time, is the only source of release
/// notes, and the running version comes from the assembly the .csproj stamps, so About, the title
/// bar and the update check cannot drift from a second, hand-maintained history.
/// </summary>
internal static class Changelog
{
    private static readonly Lazy<ChangelogEntry[]> Parsed = new(() => Parse(ReadEmbedded()));

    private static readonly Regex VersionHeading =
        new(@"^##\s+\[?(?<v>\d+\.\d+\.\d+)\]?\s*[-—–]\s*(?<d>\d{4}-\d{2}-\d{2})", RegexOptions.Compiled);

    /// <summary>Newest first.</summary>
    internal static ChangelogEntry[] Entries => Parsed.Value;

    /// <summary>The running release, e.g. "1.8.0", from the assembly's informational version.</summary>
    internal static string MarketingVersion
    {
        get
        {
            var informational = typeof(Changelog).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrWhiteSpace(informational) ? Entries[0].Version : informational.Split('+')[0];
        }
    }

    /// <summary>The notes About shows: the running version, not merely the newest heading.</summary>
    internal static ChangelogEntry? Current => Find(MarketingVersion);

    internal static ChangelogEntry? Find(string version) => Entries.FirstOrDefault(e => e.Version == version);

    /// <summary>
    /// What's New: the Main changes of every release after <paramref name="previous"/> up to <paramref name="current"/>, newest
    /// first. Without a previous release only the current one is included. A release without Main changes is skipped, except the
    /// current one, which then shows all of its notes.
    /// </summary>
    internal static IReadOnlyList<ChangelogEntry> MainChangesSince(IEnumerable<ChangelogEntry> entries, Version? previous, string current)
    {
        if (!Version.TryParse(current, out var target)) return [];
        var releases = new List<ChangelogEntry>();
        foreach (var entry in entries)
        {
            if (!Version.TryParse(entry.Version, out var version)) continue;
            if (previous is null ? version != target : version <= previous || version > target) continue;
            var main = entry.Sections.Where(s => string.Equals(s.Heading, "Main changes", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (main.Length > 0) releases.Add(entry with { Sections = main });
            else if (version == target) releases.Add(entry);
        }

        return releases;
    }

    internal static string GetReleaseNotes(string version)
    {
        var entry = Find(version);
        if (entry is null) return "No release notes available for this version.";

        var lines = new List<string>();
        foreach (var section in entry.Sections)
        {
            if (section.Heading is not null)
            {
                if (lines.Count > 0) lines.Add("");
                lines.Add(section.Heading);
            }

            lines.AddRange(section.Bullets.Select(b => "• " + b));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Parses <c>## 1.8.0 - 2026-10-20</c> headings, <c>###</c> sections and <c>-</c> bullets.</summary>
    internal static ChangelogEntry[] Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        string? version = null, date = null, heading = null;
        var sections = new List<ChangelogSection>();
        var bullets = new List<string>();

        void FlushSection()
        {
            if (bullets.Count > 0) sections.Add(new ChangelogSection(heading, bullets.ToArray()));
            bullets.Clear();
        }

        void FlushEntry()
        {
            FlushSection();
            if (version is not null) entries.Add(new ChangelogEntry(version, date!, sections.ToArray()));
            sections.Clear();
            heading = null;
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var match = VersionHeading.Match(line);
            if (match.Success)
            {
                FlushEntry();
                version = match.Groups["v"].Value;
                date = match.Groups["d"].Value;
                continue;
            }

            if (version is null) continue;
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushSection();
                heading = line[4..].Trim();
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                bullets.Add(Plain(line[2..]));
            }
            else if (line.StartsWith("  ", StringComparison.Ordinal) && bullets.Count > 0 && line.Trim().Length > 0)
            {
                bullets[^1] += " " + Plain(line);
            }
        }

        FlushEntry();
        return entries.ToArray();
    }

    // Release notes are drawn as plain text; drop Markdown emphasis and code marks.
    private static string Plain(string text) => text.Trim().Replace("**", "").Replace("`", "");

    private static string ReadEmbedded()
    {
        using var stream = typeof(Changelog).Assembly.GetManifestResourceStream("CHANGELOG.md")
            ?? throw new InvalidOperationException("CHANGELOG.md is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
