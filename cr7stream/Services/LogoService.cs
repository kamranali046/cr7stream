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
        return File.Exists(path);
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
            using var response = await client.GetAsync(externalUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Placeholder;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            // Only save if the response is genuinely an image. The extension of the
            // source URL is NOT trusted: many hosts (e.g. totalsportek) return an
            // HTML page with a 200 for missing images, which must not be stored as
            // a .webp (it renders as a broken image).
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
        // PNG
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        // GIF
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
        // WEBP (RIFF....WEBP)
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true;
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

