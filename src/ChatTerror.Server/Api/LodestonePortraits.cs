using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Api;

// Looks up character headshots on the Lodestone and keeps them on disk, so the web app can show them from 'self'.
// The most recent lookups also stay in memory.
public sealed partial class LodestonePortraits(IHttpClientFactory clients, TimeProvider time, IOptions<RelayOptions> options, ILogger<LodestonePortraits> log)
{
    public const string ClientName = "lodestone";

    private const int MaxEntries = 2000;
    private static readonly TimeSpan HitTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan MissTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan ErrorTtl = TimeSpan.FromMinutes(1);

    private readonly string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.Value.DbPath))!, "portraits");
    private readonly Lock gate = new();
    private readonly Dictionary<string, (byte[]? Image, DateTimeOffset Expires)> cache = new();
    private readonly Dictionary<string, Task<byte[]?>> pending = new();
    private readonly SemaphoreSlim outgoing = new(4);

    public static bool IsValid(string? name, string? world) =>
        name is { Length: <= 21 } && NamePattern().IsMatch(name) && world != null && WorldPattern().IsMatch(world);

    public Task<byte[]?> FindAsync(string name, string world)
    {
        var key = $"{name}@{world}".ToLowerInvariant();
        lock (gate)
        {
            if (cache.TryGetValue(key, out var cached) && cached.Expires > time.GetUtcNow())
                return Task.FromResult(cached.Image);
            if (pending.TryGetValue(key, out var running))
                return running;

            var lookup = LookupAndCacheAsync(key, name, world);
            pending[key] = lookup;
            return lookup;
        }
    }

    private async Task<byte[]?> LookupAndCacheAsync(string key, string name, string world)
    {
        // Lets FindAsync register the lookup as pending before it can finish and remove itself.
        await Task.Yield();
        var (image, expires) = await ReadDiskAsync(key);
        if (expires <= time.GetUtcNow())
        {
            try
            {
                image = await LookupAsync(name, world);
                expires = await WriteDiskAsync(key, image);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Keeps serving a stale headshot rather than none while the Lodestone is down.
                log.LogInformation("Lodestone lookup for {Key} failed: {Error}", key, ex.Message);
                expires = time.GetUtcNow() + ErrorTtl;
            }
        }

        lock (gate)
        {
            pending.Remove(key);
            if (cache.Count >= MaxEntries)
                Evict(time.GetUtcNow());
            cache[key] = (image, expires);
        }
        return image;
    }

    // Names are already limited to letters, apostrophes, hyphens and a space, so the key is a safe file name.
    private string ImagePath(string key) => Path.Combine(directory, key + ".jpg");

    private string MissPath(string key) => Path.Combine(directory, key + ".miss");

    private async Task<(byte[]? Image, DateTimeOffset Expires)> ReadDiskAsync(string key)
    {
        var image = new FileInfo(ImagePath(key));
        if (image.Exists)
            return (await File.ReadAllBytesAsync(image.FullName), image.LastWriteTimeUtc + HitTtl);
        var miss = new FileInfo(MissPath(key));
        if (miss.Exists)
            return (null, miss.LastWriteTimeUtc + MissTtl);
        return (null, DateTimeOffset.MinValue);
    }

    private async Task<DateTimeOffset> WriteDiskAsync(string key, byte[]? image)
    {
        Directory.CreateDirectory(directory);
        var (path, stale, ttl) = image != null ? (ImagePath(key), MissPath(key), HitTtl) : (MissPath(key), ImagePath(key), MissTtl);
        await File.WriteAllBytesAsync(path, image ?? []);
        var now = time.GetUtcNow();
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
        File.Delete(stale);
        return now + ttl;
    }

    private async Task<byte[]?> LookupAsync(string name, string world)
    {
        await outgoing.WaitAsync();
        try
        {
            var client = clients.CreateClient(ClientName);
            var search = $"https://na.finalfantasyxiv.com/lodestone/character/?q={Uri.EscapeDataString(name)}&worldname={Uri.EscapeDataString(world)}";
            using var response = await client.GetAsync(search);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();

            if (FindFace(await response.Content.ReadAsStringAsync(), name, world) is not { } face)
                return null;
            return await client.GetByteArrayAsync(face);
        }
        finally
        {
            outgoing.Release();
        }
    }

    internal static Uri? FindFace(string html, string name, string world)
    {
        foreach (var entry in html.Split("class=\"entry__chara__face\"").Skip(1))
        {
            var src = ImagePattern().Match(entry);
            var entryName = NameTextPattern().Match(entry);
            var entryWorld = WorldTextPattern().Match(entry);
            if (!src.Success || !entryName.Success || !entryWorld.Success)
                continue;

            // The world reads like "Gilgamesh [Aether]", after a tooltip icon.
            var worldText = WebUtility.HtmlDecode(TagPattern().Replace(entryWorld.Groups[1].Value, "")).Split('[')[0].Trim();
            if (!string.Equals(WebUtility.HtmlDecode(entryName.Groups[1].Value).Trim(), name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(worldText, world, StringComparison.OrdinalIgnoreCase))
                continue;

            if (Uri.TryCreate(WebUtility.HtmlDecode(src.Groups[1].Value), UriKind.Absolute, out var uri) && IsLodestoneImage(uri))
                return uri;
        }
        return null;
    }

    private static bool IsLodestoneImage(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.Host.EndsWith(".finalfantasyxiv.com", StringComparison.OrdinalIgnoreCase);

    private void Evict(DateTimeOffset now)
    {
        foreach (var (key, entry) in cache.ToList())
        {
            if (entry.Expires <= now)
                cache.Remove(key);
        }
        if (cache.Count >= MaxEntries)
            cache.Remove(cache.MinBy(entry => entry.Value.Expires).Key);
    }

    [GeneratedRegex(@"^[A-Za-z'\-]+ [A-Za-z'\-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[A-Za-z]{1,16}$")]
    private static partial Regex WorldPattern();

    [GeneratedRegex("<img[^>]*\\ssrc=\"([^\"]+)\"")]
    private static partial Regex ImagePattern();

    [GeneratedRegex("<p class=\"entry__name\">([^<]*)</p>")]
    private static partial Regex NameTextPattern();

    [GeneratedRegex("<p class=\"entry__world\">(.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex WorldTextPattern();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();
}
