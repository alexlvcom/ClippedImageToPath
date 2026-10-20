using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var payload = Encoding.UTF8.GetBytes("test installer payload");
var digest = Convert.ToHexString(SHA256.HashData(payload));
const string root = "https://github.com/alexlvcom/ClippedImageToPath/releases/download/v1.8.0/";
JsonElement Release(string tag = "v1.8.0", bool prerelease = false, string? hash = null, string name = UpdateService.InstallerName, string? url = null) =>
    JsonSerializer.SerializeToElement(new
    {
        tag_name = tag, draft = false, prerelease,
        assets = new object[] {
            new { name, browser_download_url = url ?? root + name, digest = hash is null ? null : "sha256:" + hash },
            new { name = "SHA256SUMS.txt", browser_download_url = root + "SHA256SUMS.txt", digest = (string?)null },
        }
    });
void Check(bool pass, string message) { if (!pass) throw new Exception(message); Console.WriteLine("PASS: " + message); }
var now = new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc);
Check(UpdateSchedule.NextCheck(now, UpdateCheckFrequency.Hourly) == now.AddHours(1), "hourly schedule");
Check(UpdateSchedule.NextCheck(now, UpdateCheckFrequency.Daily) == now.AddDays(1), "daily default schedule");
Check(UpdateSchedule.NextCheck(now, UpdateCheckFrequency.Weekly) == now.AddDays(7), "weekly schedule");
Check(UpdateSchedule.NextCheck(now, UpdateCheckFrequency.Monthly) == new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc), "calendar month handles month end");
Check(UpdateSchedule.NextCheck(now, UpdateCheckFrequency.Never) == DateTime.MaxValue, "Never disables scheduled checks");
Check(UpdateSchedule.Normalize((UpdateCheckFrequency)99) == UpdateCheckFrequency.Daily, "invalid saved frequency falls back to daily");
var current = new Version(1, 7, 0);
if (args.Contains("--live"))
{
    using var live = new UpdateService();
    var release = await live.CheckAsync(current, default) ?? throw new Exception("No live update available");
    var liveCache = Path.Combine(Path.GetTempPath(), "clipped-live-update-" + Guid.NewGuid().ToString("N"));
    try
    {
        var installer = await live.DownloadAsync(release, liveCache, default);
        Check(new FileInfo(installer).Length > 0, $"live GitHub {release.Version} installer downloaded and verified");
    }
    finally { if (Directory.Exists(liveCache)) Directory.Delete(liveCache, true); }
}
Check(UpdateService.ParseRelease(Release(), current)?.Version == new Version(1, 8, 0), "newer stable release detected");
Check(UpdateService.ParseRelease(Release("v1.7.0"), current) is null, "same version ignored");
Check(UpdateService.ParseRelease(Release("v1.6.4"), current) is null, "older release ignored");
Check(UpdateService.ParseRelease(Release(prerelease: true), current) is null, "prerelease ignored");
Check(UpdateService.ParseRelease(Release(name: "ClippedImageToPath.exe"), current) is null, "portable-only release ignored");
try { UpdateService.ParseRelease(Release(url: "https://example.com/setup.exe"), current); throw new Exception("unsafe URL accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS: foreign installer URL rejected"); }
var cache = Path.Combine(Path.GetTempPath(), "clipped-update-tests-" + Guid.NewGuid().ToString("N"));
try
{
    using var service = new UpdateService(new FakeHandler(payload, digest));
    var update = (await service.CheckAsync(current, default))!;
    var path = await service.DownloadAsync(update, cache, default);
    Check(File.ReadAllBytes(path).SequenceEqual(payload), "installer downloaded and SHA-256 verified");
    var fallback = UpdateService.ParseRelease(Release(), current)!;
    var fallbackPath = await service.DownloadAsync(fallback, cache, default);
    Check(File.Exists(fallbackPath), "SHA256SUMS fallback supported");
    var bad = update with { Sha256 = new string('0', 64) };
    var before = Directory.GetFiles(cache, "*.exe", SearchOption.AllDirectories).Length;
    try { await service.DownloadAsync(bad, cache, default); throw new Exception("bad checksum accepted"); }
    catch (InvalidDataException) { Console.WriteLine("PASS: checksum mismatch rejected"); }
    Check(Directory.GetFiles(cache, "*.exe", SearchOption.AllDirectories).Length == before, "failed download removed");
    using var cts = new CancellationTokenSource(); cts.Cancel();
    try { await service.DownloadAsync(update, cache, cts.Token); throw new Exception("cancellation ignored"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: download cancellation supported"); }
    using var missing = new UpdateService(new FakeHandler(payload, digest, HttpStatusCode.NotFound));
    Check(await missing.CheckAsync(current, default) is null, "missing release handled");
    using var rateLimited = new UpdateService(new FakeHandler(payload, digest, HttpStatusCode.Forbidden));
    try { await rateLimited.CheckAsync(current, default); throw new Exception("API failure ignored"); }
    catch (HttpRequestException) { Console.WriteLine("PASS: API failure reported to caller"); }
}
finally { if (Directory.Exists(cache)) Directory.Delete(cache, true); }

// Download progress feeds the update dialog's progress bar.
var progressCache = Path.Combine(Path.GetTempPath(), "clipped-progress-tests-" + Guid.NewGuid().ToString("N"));
try
{
    using var service = new UpdateService(new FakeHandler(payload, digest));
    var update = (await service.CheckAsync(current, default))!;
    long reported = 0;
    await service.DownloadAsync(update, progressCache, default, new SyncProgress(p => reported = p.Received));
    Check(reported == payload.Length, "download progress reports the received bytes");
}
finally { if (Directory.Exists(progressCache)) Directory.Delete(progressCache, true); }

// Settings: the theme setting and the Settings draft.
var settingsFolder = Path.Combine(Path.GetTempPath(), "clipped-settings-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var settingsPath = Path.Combine(settingsFolder, "settings.json");
    Check(new AppSettings().Theme == AlexLV.WindowsUi.AppTheme.System, "the theme follows Windows by default");

    var saved = AppSettings.Load(settingsPath);
    saved.Theme = AlexLV.WindowsUi.AppTheme.Dark;
    saved.Save();
    Check(AppSettings.Load(settingsPath).Theme == AlexLV.WindowsUi.AppTheme.Dark, "the chosen theme survives a save and load");
    Check(!File.Exists(settingsPath + ".tmp"), "an atomic save leaves no temporary file");

    var json = File.ReadAllText(settingsPath);
    File.WriteAllText(settingsPath, System.Text.RegularExpressions.Regex.Replace(json, "\"Theme\":\\s*\\d+", "\"Theme\": 42"));
    Check(AppSettings.Load(settingsPath).Theme == AlexLV.WindowsUi.AppTheme.System, "a hand-edited unknown theme falls back to System");

    var running = new AppSettings { Theme = AlexLV.WindowsUi.AppTheme.Light };
    running.RemoteServers.Add(new RemoteServer { Name = "dev-box", Host = "dev.example" });
    var draft = running.Clone();
    draft.Theme = AlexLV.WindowsUi.AppTheme.Dark;
    draft.RemoteServers[0].Host = "changed.example";
    draft.RemoteServers.Add(new RemoteServer { Name = "second" });
    Check(running.Theme == AlexLV.WindowsUi.AppTheme.Light && running.RemoteServers.Count == 1
          && running.RemoteServers[0].Host == "dev.example", "a Settings draft leaves the running settings unchanged");
}
finally { if (Directory.Exists(settingsFolder)) Directory.Delete(settingsFolder, true); }

// The repository files the app is built from.
var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "ClippedImageToPath.csproj"))) repo = repo.Parent;
if (repo is null) throw new Exception("repository root not found");
var project = File.ReadAllText(Path.Combine(repo.FullName, "ClippedImageToPath.csproj"));

// WPF creates real cultures when it measures text; invariant globalization crashed Tray Keeper's first TextBox.
Check(!project.Contains("<InvariantGlobalization>true</InvariantGlobalization>"), "the app keeps culture data for WPF text layout");

// An up-to-date result names the running version and never talks about installers.
Check(UpdateMessages.Latest("1.8.0") == "You're running the latest version (v1.8.0)", "up-to-date wording names the running version");
Check(UpdateMessages.LastChecked(new DateTime(2026, 10, 20, 13, 5, 0)) == "Last checked 2026-10-20 13:05", "the status card shows the check time");
var appSource = File.ReadAllText(Path.Combine(repo.FullName, "Program.cs")) + File.ReadAllText(Path.Combine(repo.FullName, "Ui", "SettingsWindow.xaml.cs"));
Check(!appSource.Contains("No newer installer", StringComparison.OrdinalIgnoreCase), "no 'No newer installer' wording remains");
// A manual check reports in the Updates card: no message dialog for an up-to-date or failed result.
var checkFlow = appSource[appSource.IndexOf("private async Task<UpdateStatus> RunUpdateCheckAsync", StringComparison.Ordinal)..];
checkFlow = checkFlow[..checkFlow.IndexOf("private void ShowUpdateDialog", StringComparison.Ordinal)];
Check(!checkFlow.Contains("MessageDialog.Show"), "update check results open no message dialog");

// One version source: the .csproj version must have notes in CHANGELOG.md, which About and the title read.
var projectVersion = System.Text.RegularExpressions.Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value;
var entries = Changelog.Parse(File.ReadAllText(Path.Combine(repo.FullName, "CHANGELOG.md")));
Check(entries.Length > 0 && entries[0].Version == projectVersion, $"newest CHANGELOG.md entry matches the project version {projectVersion}");
foreach (var tag in new[] { "<InformationalVersion>", "<FileVersion>", "<AssemblyVersion>" })
{
    var value = System.Text.RegularExpressions.Regex.Match(project, tag + "([^<]+)<").Groups[1].Value;
    Check(value == projectVersion || value == projectVersion + ".0", $"{tag.Trim('<', '>')} matches {projectVersion}");
}
Check(entries[0].Sections.SelectMany(s => s.Bullets).All(b => !b.Contains("**")), "release notes are shown without Markdown emphasis");
Check(Changelog.Parse("## 2.0.0 - 2026-01-01\n\n### Main changes\n\n- **Bold:** text\n\n## 1.9.0 - 2025-12-01\n\n- old")[1].Version == "1.9.0",
    "changelog parsing finds every release");

sealed class SyncProgress(Action<(long Received, long? Total)> report) : IProgress<(long Received, long? Total)>
{
    public void Report((long Received, long? Total) value) => report(value);
}

sealed class FakeHandler(byte[] payload, string digest, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Headers.UserAgent.Count == 0) throw new Exception("Missing GitHub User-Agent");
        var url = request.RequestUri!.AbsoluteUri;
        var content = url.Contains("api.github.com")
            ? JsonSerializer.Serialize(new {
                tag_name = "v1.8.0", draft = false, prerelease = false,
                assets = new[] { new { name = UpdateService.InstallerName, browser_download_url = "https://github.com/alexlvcom/ClippedImageToPath/releases/download/v1.8.0/" + UpdateService.InstallerName, digest = "sha256:" + digest } }
            }) : digest + "  " + UpdateService.InstallerName;
        return Task.FromResult(new HttpResponseMessage(status) {
            Content = url.EndsWith(".exe") ? new ByteArrayContent(payload) : new StringContent(content)
        });
    }
}
