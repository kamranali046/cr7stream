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
}
