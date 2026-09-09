using System.Net;
using System.Text;
using GPTAutoResume.Core;
using Xunit;

namespace GPTAutoResume.Tests;

public sealed class BannerServiceTests
{
    [Fact]
    public void BannerArtworkLinkUsesRequestedWebtoonsPage()
    {
        Assert.Equal("https://www.webtoons.com/zh-hant/canvas/%E9%87%91%E5%90%89%E6%8B%89%E4%BD%8E%E8%B3%BD/list?title_no=46832", AppUrls.BannerArtworkLink);
    }

    [Fact]
    public async Task DisabledBannerDoesNotCallNetwork()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Network should not be called."));
        var service = new BannerService(new HttpClient(handler), NewTempDir());
        var fallback = NewFallback();

        var result = await service.GetBannerAsync(networkEnabled: false, refreshMinutes: 45, fallback);

        Assert.Equal(fallback, result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ManifestAllowsOnlySafeImageNames()
    {
        var imageBytes = Encoding.UTF8.GetBytes("fake image");
        var handler = new FakeHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/list.php", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Json("""
                    {
                      "version": 1,
                      "images": [
                        "cat01.webp",
                        "../secret.png",
                        "payload.exe",
                        "cat02.svg",
                        "cat03.jpg"
                      ]
                    }
                    """);
            }

            Assert.Contains(request.RequestUri?.AbsolutePath ?? "", new[] { "/img/banner/gptauto/cat01.webp", "/img/banner/gptauto/cat03.jpg" });
            return Bytes(imageBytes, "image/webp");
        });
        var service = new BannerService(new HttpClient(handler), NewTempDir());

        var result = await service.GetBannerAsync(networkEnabled: true, refreshMinutes: 45, NewFallback());

        Assert.True(result.EndsWith("cat01.webp", StringComparison.OrdinalIgnoreCase)
            || result.EndsWith("cat03.jpg", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("..", result);
        Assert.False(result.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NetworkFailureUsesCachedImage()
    {
        var cacheDir = NewTempDir();
        var cached = Path.Combine(cacheDir, "cat-cache.png");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(cached, "cached");
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("offline"));
        var service = new BannerService(new HttpClient(handler), cacheDir);

        var result = await service.GetBannerAsync(networkEnabled: true, refreshMinutes: 45, NewFallback());

        Assert.Equal(cached, result);
    }

    [Fact]
    public async Task ManifestJsonIsUsedWhenListPhpIsMissing()
    {
        var handler = new FakeHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/list.php", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Json("""{"version":1,"images":["cat-json.webp"]}""");
            }

            Assert.EndsWith("/img/banner/gptauto/cat-json.webp", request.RequestUri?.AbsolutePath ?? "", StringComparison.OrdinalIgnoreCase);
            return Bytes(Encoding.UTF8.GetBytes("fake image"), "image/webp");
        });
        var service = new BannerService(new HttpClient(handler), NewTempDir());

        var result = await service.GetBannerAsync(networkEnabled: true, refreshMinutes: 45, NewFallback());

        Assert.EndsWith("cat-json.webp", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FallbackDoesNotBlockNextOnlineRefresh()
    {
        var online = false;
        var handler = new FakeHandler((request, _) =>
        {
            if (!online)
            {
                throw new HttpRequestException("offline");
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/list.php", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Json("""{"version":1,"images":["cat-online.png"]}""");
            }

            return Bytes(Encoding.UTF8.GetBytes("fake image"), "image/png");
        });
        var service = new BannerService(new HttpClient(handler), NewTempDir());
        var fallback = NewFallback();

        var offlineResult = await service.GetBannerAsync(networkEnabled: true, refreshMinutes: 45, fallback);
        online = true;
        var onlineResult = await service.GetBannerAsync(networkEnabled: true, refreshMinutes: 45, fallback);

        Assert.Equal(fallback, offlineResult);
        Assert.EndsWith("cat-online.png", onlineResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForceRefreshRedownloadsSameFilename()
    {
        var cacheDir = NewTempDir();
        var cached = Path.Combine(cacheDir, "cat-same.png");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(cached, "old image");
        var imageRequests = 0;
        var handler = new FakeHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/list.php", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Json("""{"version":1,"images":["cat-same.png"]}""");
            }

            imageRequests++;
            return Bytes(Encoding.UTF8.GetBytes("new image"), "image/png");
        });
        var service = new BannerService(new HttpClient(handler), cacheDir);

        var result = await service.GetBannerAsync(networkEnabled: true, refreshMinutes: 45, NewFallback(), forceRefresh: true);

        Assert.Equal(cached, result);
        Assert.Equal(1, imageRequests);
        Assert.Equal("new image", await File.ReadAllTextAsync(cached));
    }

    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "GPTAutoResume.Tests", Guid.NewGuid().ToString("N"));

    private static string NewFallback()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "fallback.png");
        File.WriteAllText(path, "fallback");
        return path;
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Bytes(byte[] bytes, string mediaType) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) }
        }
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responder(request, cancellationToken));
        }
    }
}
