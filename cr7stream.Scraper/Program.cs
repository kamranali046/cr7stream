using System.Text.Json;
using cr7stream.Logic.Models;
using cr7stream.Logic.Scrapers;

// Console runner that scrapes total-sportek.st and writes fixtures.json.
// Usage: dotnet run --project cr7stream.Scraper [optional output path]

var outputPath = args.Length > 0
    ? args[0]
    : FindDefaultOutputPath();

Console.WriteLine($"Scraping https://total-sportek.st/ ...");
Console.WriteLine($"Output : {outputPath}");

using var http = new HttpClient();
var scraper = new TotalSportekScraper(http);

var data = await scraper.ScrapeAsync();

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

Console.WriteLine($"Done. {data.Matches.Count} matches across {data.Leagues.Count} categories, {data.Sports.Count} sports.");

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


