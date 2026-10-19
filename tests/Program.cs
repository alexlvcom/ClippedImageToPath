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
