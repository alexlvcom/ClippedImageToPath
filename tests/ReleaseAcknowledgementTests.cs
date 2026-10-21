using System.IO;
using ClippedImageToPath.Ui;
internal static class ReleaseAcknowledgementTests
{
    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ReleaseAcknowledgementTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "last-seen-release.txt");
        var settings = Path.Combine(folder, "settings.json");
        static void Check(bool ok, string what = "Release acknowledgement regression") { if (!ok) throw new Exception(what); }
        try
        {
            var state = new WhatsNewState(path, settings);
            Check(state.ShouldShow("1.0.0"));
            Check(state.PreviousRelease() is null, "no record and no settings is a fresh installation");
            state.MarkShown("1.0.0");
            Check(!new WhatsNewState(path, settings).ShouldShow("1.0.0"));
            Check(!state.ShouldShow("0.9.0"));
            Check(state.ShouldShow("1.0.1"));
            Check(state.PreviousRelease() == new Version(1, 0, 0), "the acknowledged release is the previous one");
            File.WriteAllText(path, "corrupt");
            Check(state.ShouldShow("1.0.0"));
            Check(state.PreviousRelease() is null, "a corrupt record shows only the current release");

            // Releases before 1.8.2 kept no record: their settings tell which release was installed.
            File.Delete(path);
            File.WriteAllText(settings, "{ \"OutputDirectory\": \"C:\\Temp\", \"Theme\": 0 }");
            Check(state.PreviousRelease() == new Version(1, 8, 1), "settings with Theme come from 1.8.0 or 1.8.1");
            File.WriteAllText(settings, "{ \"OutputDirectory\": \"C:\\Temp\" }");
            Check(state.PreviousRelease() == new Version(1, 7, 3), "settings without Theme predate 1.8.0");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        var entries = Changelog.Parse(
            "## 1.8.3 - 2026-10-21\n\n### Main changes\n\n- c\n\n### Other updates\n\n- c2\n\n" +
            "## 1.8.2 - 2026-10-20\n\n### Main changes\n\n- b\n\n" +
            "## 1.8.1 - 2026-10-20\n\n### Other updates\n\n- small\n\n" +
            "## 1.8.0 - 2026-10-20\n\n### Main changes\n\n- a\n");
        string Listed(Version? previous, string current) =>
            string.Join(",", Changelog.MainChangesSince(entries, previous, current).Select(e => e.Version + ":" + string.Join("|", e.Sections.SelectMany(s => s.Bullets))));
        Check(Listed(null, "1.8.3") == "1.8.3:c", "a fresh installation shows only the current release's main changes");
        Check(Listed(new Version(1, 8, 0), "1.8.3") == "1.8.3:c,1.8.2:b", "skipped releases are listed newest first, without Other updates");
        Check(Listed(new Version(1, 7, 3), "1.8.2") == "1.8.2:b,1.8.0:a", "releases after the current one are not listed");
        Check(Listed(new Version(1, 8, 0), "1.8.1") == "1.8.1:small", "a current release without Main changes shows all its notes");
    }
}
