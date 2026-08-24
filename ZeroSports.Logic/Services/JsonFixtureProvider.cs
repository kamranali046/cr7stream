using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using ZeroSports.Logic.Models;

namespace ZeroSports.Logic.Services;

public class JsonFixtureProvider : IFixtureProvider
{
    private readonly IWebHostEnvironment _environment;
    private const string RelativePath = "data/fixtures.json";

    public JsonFixtureProvider(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<FixtureData> LoadRawAsync()
    {
        var path = Path.Combine(_environment.WebRootPath, RelativePath);

        if (!File.Exists(path))
        {
            return new FixtureData();
        }

        await using var stream = File.OpenRead(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var data = await JsonSerializer.DeserializeAsync<FixtureData>(stream, options);
        return data ?? new FixtureData();
    }

    public async Task SaveAsync(FixtureData data, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_environment.WebRootPath, RelativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken);
    }
}
