using System.IO;
using ClippedImageToPath.Ui;
internal static class ReleaseAcknowledgementTests
{
    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ReleaseAcknowledgementTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "last-seen-release.txt");
        static void Check(bool ok) { if (!ok) throw new Exception("Release acknowledgement regression"); }
        try
        {
            var state = new WhatsNewState(path);
            Check(state.ShouldShow("1.0.0"));
            state.MarkShown("1.0.0");
            Check(!new WhatsNewState(path).ShouldShow("1.0.0"));
            Check(!state.ShouldShow("0.9.0"));
            Check(state.ShouldShow("1.0.1"));
            File.WriteAllText(path, "corrupt");
            Check(state.ShouldShow("1.0.0"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
