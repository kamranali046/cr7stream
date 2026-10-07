using System.Text.Json;
using cr7stream.Logic.Models;
using cr7stream.Logic.Scrapers;
using cr7stream.Logic.Services;

// Console runner that scrapes https://totalsportek1.is/ and writes fixtures.json.
// Usage: dotnet run --project cr7stream.Scraper [optional output path] [--drill]

var outputPath = args.Length > 0 && !args[0].StartsWith("--")
    ? args[0]
    : FindDefaultOutputPath();
var drill = args.Contains("--drill");

Console.WriteLine($"Scraping https://totalsportek1.is/ ...");
Console.WriteLine($"Output : {outputPath}");

using var http = new HttpClient();
var scraper = new TotalSportekScraper(http, logoService: new NoopLogoService());

var data = await scraper.ScrapeAsync(drillPlayers: drill);

data.NormalizeTimes = false;
data.ScrapedAtUtc = DateTime.UtcNow;

var directory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrEmpty(directory))
{
    Directory.CreateDirectory(directory);
}

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
};

await using (var stream = File.Create(outputPath))
{
    await JsonSerializer.SerializeAsync(stream, data, options);
}

var playersTotal = data.Matches.Sum(m => m.Players.Count);
Console.WriteLine($"Done. {data.Matches.Count} matches across {data.Leagues.Count} categories, {data.Sports.Count} sports, {playersTotal} players.");

static string FindDefaultOutputPath()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "cr7stream.slnx")))
        {
            return Path.Combine(dir, "cr7stream", "wwwroot", "data", "fixtures.json");
        }

        var parent = Directory.GetParent(dir);
        if (parent is null) break;
        dir = parent.FullName;
    }

    // Fallback: next to this executable.
    return Path.Combine(AppContext.BaseDirectory, "fixtures.json");
}

// Logo downloading happens in the web app; the console runner only parses fixtures.
file sealed class NoopLogoService : ILogoService
{
    public string GetLocalPath(string slug) => string.Empty;
    public bool LocalFileExists(string slug) => false;
    public Task<string> GetOrDownloadAsync(string? externalUrl, string slug, CancellationToken ct = default)
        => Task.FromResult(string.Empty);
}


