using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using ZeroSports.Logic.Models;

namespace ZeroSports.Logic.Services;

public interface IScraperSettingsProvider
{
    Task<ScraperSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ScraperSettings settings, CancellationToken cancellationToken = default);
}

public class ScraperSettingsProvider : IScraperSettingsProvider
{
    private readonly IWebHostEnvironment _environment;
    private const string RelativePath = "data/scraper-settings.json";

    public ScraperSettingsProvider(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<ScraperSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_environment.WebRootPath, RelativePath);

        if (!File.Exists(path))
        {
            return new ScraperSettings();
        }

        await using var stream = File.OpenRead(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = await JsonSerializer.DeserializeAsync<ScraperSettings>(stream, options);
        return data ?? new ScraperSettings();
    }

    public async Task SaveAsync(ScraperSettings settings, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_environment.WebRootPath, RelativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, settings, options, cancellationToken);
    }
}
