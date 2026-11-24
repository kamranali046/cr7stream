using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using cr7stream.Logic.Models;

namespace cr7stream.Logic.Services;

public interface IScraperSettingsProvider
{
    Task<ScraperSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ScraperSettings settings, CancellationToken cancellationToken = default);
}

public class ScraperSettingsProvider : IScraperSettingsProvider
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private const string RelativePath = "scraper-settings.json";

    public ScraperSettingsProvider(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    private string DataDir
    {
        get
        {
            var configured = _configuration["DataDir"]
                             ?? Environment.GetEnvironmentVariable("DATA_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
            return Path.Combine(_environment.ContentRootPath, "wwwroot", "data");
        }
    }

    private string FilePath => Path.Combine(DataDir, RelativePath);

    public async Task<ScraperSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = FilePath;

        if (!File.Exists(path))
        {
            return new ScraperSettings();
        }

        await using var stream = File.OpenRead(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = await JsonSerializer.DeserializeAsync<ScraperSettings>(stream, options, cancellationToken);
        return data ?? new ScraperSettings();
    }

    public async Task SaveAsync(ScraperSettings settings, CancellationToken cancellationToken = default)
    {
        var path = FilePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, options, cancellationToken);
        }
        File.Move(tempPath, path, overwrite: true);
    }
}
