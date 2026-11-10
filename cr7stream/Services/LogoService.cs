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
        var path = Path.Combine(_env.ContentRootPath, "wwwroot", LogoDir, SlugToFileName(slug));
        if (!File.Exists(path)) return false;
        try
        {
            var bytes = File.ReadAllBytes(path);
            return IsImageBytes(bytes);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetOrDownloadAsync(string? externalUrl, string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalUrl) || string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        var fileName = SlugToFileName(slug);
        var dir = Path.Combine(_env.ContentRootPath, "wwwroot", LogoDir);
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            client.DefaultRequestHeaders.Referrer = new Uri("https://totalsportekl.is/");
            client.DefaultRequestHeaders.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            using var response = await client.GetAsync(externalUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Placeholder;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            // Only save if the response is genuinely an image. The source URL
            // extension is NOT trusted (totalsportek returns an HTML page with a
            // 200 for missing images).
            if (bytes.Length > 0 && bytes.Length < 2 * 1024 * 1024 &&
                contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                IsImageBytes(bytes))
            {
                await File.WriteAllBytesAsync(localPath, bytes, ct);
                return GetLocalPath(slug);
            }

            return string.Empty;
        }
        catch
        {
            return Placeholder;
        }
    }

    private static bool IsImageBytes(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 4) return false;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true; // PNG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true; // JPEG
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true; // GIF
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true; // WEBP
        return false;
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

