using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace GPTAutoResume;

public sealed class BannerService
{
    private const string AllowedHost = "easylifehub.net";
    private const string ManifestUrl = "https://easylifehub.net/img/banner/gptauto/list.php";
    private const string JsonManifestUrl = "https://easylifehub.net/img/banner/gptauto/manifest.json";
    private const string BaseImageUrl = "https://easylifehub.net/img/banner/gptauto/";
    private const long MaxImageBytes = 3 * 1024 * 1024;
    private static readonly TimeSpan ManifestTtl = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ImageTtl = TimeSpan.FromDays(7);
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private readonly HttpClient _client;
    private readonly string _cacheDir;
    private readonly string _manifestCachePath;
    private readonly string _statusPath;
    private string? _lastImage;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private bool _lastImageWasFallback;

    public BannerService()
        : this(
            new HttpClient { Timeout = TimeSpan.FromSeconds(8) },
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GPTAutoResume",
                "banner-cache"))
    {
    }

    public BannerService(HttpClient client, string cacheDir)
    {
        _client = client;
        _cacheDir = cacheDir;
        _manifestCachePath = Path.Combine(_cacheDir, "manifest.json");
        _statusPath = Path.Combine(_cacheDir, "banner-status.txt");
    }

    public async Task<string> GetBannerAsync(bool networkEnabled, int refreshMinutes, string fallbackPath, bool forceRefresh = false)
    {
        if (!networkEnabled)
        {
            WriteStatus("Banner disabled. Network skipped.");
            return fallbackPath;
        }

        var now = DateTimeOffset.Now;
        if (!forceRefresh
            && _lastImage is not null
            && !_lastImageWasFallback
            && now - _lastRefresh < TimeSpan.FromMinutes(Math.Max(30, refreshMinutes)))
        {
            return _lastImage;
        }

        Directory.CreateDirectory(_cacheDir);
        var manifest = await TryGetManifestAsync(forceRefresh);
        WriteStatus($"Manifest images: {manifest.Count}");
        var selected = ChooseImage(manifest);
        if (selected is not null)
        {
            WriteStatus($"Selected image: {selected}");
            var imagePath = await TryGetImageAsync(selected, forceRefresh);
            if (imagePath is not null)
            {
                WriteStatus($"Using remote/cache image: {imagePath}");
                _lastImage = imagePath;
                _lastImageWasFallback = false;
                _lastRefresh = now;
                return imagePath;
            }
        }

        var cached = PickCachedImage();
        WriteStatus(cached is null ? "No cached image. Using bundled fallback." : $"Using cached image after request failure: {cached}");
        _lastImage = cached ?? fallbackPath;
        _lastImageWasFallback = cached is null;
        _lastRefresh = now;
        return _lastImage;
    }

    private async Task<IReadOnlyList<string>> TryGetManifestAsync(bool forceRefresh)
    {
        if (!forceRefresh
            && File.Exists(_manifestCachePath)
            && DateTimeOffset.Now - File.GetLastWriteTimeUtc(_manifestCachePath) < ManifestTtl)
        {
            return ReadManifest(File.ReadAllText(_manifestCachePath));
        }

        try
        {
            var names = await FetchManifestNamesAsync(WithAppCacheKey(ManifestUrl));
            if (names.Count == 0)
            {
                names = await FetchManifestNamesAsync(WithAppCacheKey(JsonManifestUrl));
            }

            if (names.Count > 0)
            {
                File.WriteAllText(_manifestCachePath, JsonSerializer.Serialize(new BannerManifest
                {
                    Version = 1,
                    Images = names.ToArray()
                }));
            }

            return names;
        }
        catch
        {
            WriteStatus("Manifest request failed. Falling back to cached manifest if present.");
            return File.Exists(_manifestCachePath)
                ? ReadManifest(File.ReadAllText(_manifestCachePath))
                : [];
        }
    }

    private async Task<IReadOnlyList<string>> FetchManifestNamesAsync(string url)
    {
        using var response = await _client.GetAsync(url);
        WriteStatus($"Manifest request: {url} -> {(int)response.StatusCode}");
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        return ReadManifest(await response.Content.ReadAsStringAsync());
    }

    private static string WithAppCacheKey(string url)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}app=gpt-auto-resume-v1";
    }

    private static IReadOnlyList<string> ReadManifest(string json)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<BannerManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return manifest?.Images?
                .Where(IsSafeImageName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private string? ChooseImage(IReadOnlyList<string> images)
    {
        if (images.Count == 0)
        {
            return null;
        }

        if (images.Count == 1)
        {
            return images[0];
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var candidate = images[RandomNumberGenerator.GetInt32(images.Count)];
            if (!string.Equals(candidate, Path.GetFileName(_lastImage), StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return images[RandomNumberGenerator.GetInt32(images.Count)];
    }

    private async Task<string?> TryGetImageAsync(string filename, bool forceRefresh)
    {
        if (!IsSafeImageName(filename))
        {
            return null;
        }

        var destination = Path.Combine(_cacheDir, filename);
        if (!forceRefresh
            && File.Exists(destination)
            && DateTimeOffset.Now - File.GetLastWriteTimeUtc(destination) < ImageTtl)
        {
            return destination;
        }

        try
        {
            var imageUri = new Uri(new Uri(BaseImageUrl), filename);
            if (!IsAllowedImageUri(imageUri))
            {
                WriteStatus($"Rejected image URI: {imageUri}");
                return null;
            }

            using var response = await _client.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead);
            WriteStatus($"Image request: {imageUri} -> {(int)response.StatusCode}");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxImageBytes)
            {
                WriteStatus($"Rejected oversized image: {filename}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(destination);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                total += read;
                if (total > MaxImageBytes)
                {
                    file.Close();
                    File.Delete(destination);
                    return null;
                }

                await file.WriteAsync(buffer.AsMemory(0, read));
            }

            return destination;
        }
        catch (Exception ex)
        {
            WriteStatus($"Image request failed for {filename}: {ex.GetType().Name} {ex.Message}");
            return File.Exists(destination) ? destination : null;
        }
    }

    private string? PickCachedImage()
    {
        if (!Directory.Exists(_cacheDir))
        {
            return null;
        }

        var files = Directory.EnumerateFiles(_cacheDir)
            .Where(path => IsAllowedExtension(Path.GetExtension(path)))
            .ToArray();
        return files.Length == 0 ? null : files[RandomNumberGenerator.GetInt32(files.Length)];
    }

    private static bool IsAllowedImageUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase)
        && IsAllowedExtension(Path.GetExtension(uri.AbsolutePath));

    private static bool IsSafeImageName(string filename) =>
        !string.IsNullOrWhiteSpace(filename)
        && filename == Path.GetFileName(filename)
        && !filename.Contains("..", StringComparison.Ordinal)
        && IsAllowedExtension(Path.GetExtension(filename));

    private static bool IsAllowedExtension(string extension) =>
        AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private void WriteStatus(string message)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.AppendAllText(_statusPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}{Environment.NewLine}");
        }
        catch
        {
            // Banner diagnostics must never affect monitoring.
        }
    }

    private sealed class BannerManifest
    {
        public int Version { get; set; }
        public string[]? Images { get; set; }
    }
}
