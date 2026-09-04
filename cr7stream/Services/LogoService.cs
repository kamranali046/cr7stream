using cr7stream.Logic.Services;

namespace cr7stream.Services;

public class LogoService : ILogoService
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpClientFactory _httpFactory;
    private const string LogoDir = "img/logos";
    private const string Ext = ".webp";
    private const string Placeholder = "/img/category-placeholder.svg";

    public LogoService(IWebHostEnvironment env, IHttpClientFactory httpFactory)
    {
        _env = env;
        _httpFactory = httpFactory;
    }

    public string GetLocalPath(string slug)
    {
        return $"/{LogoDir}/{SlugToFileName(slug)}";
    }

    public bool LocalFileExists(string slug)
    {
        var path = Path.Combine(_env.WebRootPath, LogoDir, SlugToFileName(slug));
        return File.Exists(path);
    }

    public async Task<string> GetOrDownloadAsync(string? externalUrl, string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalUrl) || string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        var fileName = SlugToFileName(slug);
        var dir = Path.Combine(_env.WebRootPath, LogoDir);
        Directory.CreateDirectory(dir);
        var localPath = Path.Combine(dir, fileName);

        // Already downloaded
        if (File.Exists(localPath))
        {
            return GetLocalPath(slug);
        }

        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.GetAsync(externalUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Placeholder;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            // Only save if it looks like an image (< 2MB)
            if (bytes.Length > 0 && bytes.Length < 2 * 1024 * 1024 &&
                (contentType.StartsWith("image/") || externalUrl.Contains(".webp") || externalUrl.Contains(".png") || externalUrl.Contains(".jpg") || externalUrl.Contains(".jpeg")))
            {
                await File.WriteAllBytesAsync(localPath, bytes, ct);
                return GetLocalPath(slug);
            }

            return Placeholder;
        }
        catch
        {
            return Placeholder;
        }
    }

    private static string SlugToFileName(string slug)
    {
        var safe = slug.ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '-');
        }
        return safe + Ext;
    }
}

