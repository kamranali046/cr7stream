using ZeroSports.Logic.Models;
using ZeroSports.Logic.Services;

namespace ZeroSports.Logic;

public interface IScrapperLogic
{
    Task<FixtureData> GetFixturesAsync();
    Task<List<Sport>> GetSportsAsync();
    Task<List<League>> GetLeaguesAsync();
    Task<List<Team>> GetTeamsAsync();
    Task<Match?> GetMatchBySlugAsync(string slug);
}

public class ScrapperLogic : IScrapperLogic
{
    private readonly IFixtureProvider _provider;
    private readonly TimeSpan _liveWindow = TimeSpan.FromHours(3);

    public ScrapperLogic(IFixtureProvider provider)
    {
        _provider = provider;
    }

    public async Task<FixtureData> GetFixturesAsync()
    {
        var raw = await _provider.LoadRawAsync();

        // The dummy JSON is anchored to a fixed reference time. To keep the demo
        // always showing live events and realistic countdowns regardless of when it
        // is opened, we rebase every timestamp relative to the current UTC time.
        // When a real scraper replaces the JSON source this block simply becomes a
        // pass-through (or the place where raw HTML is parsed into FixtureData).
        var drift = DateTime.UtcNow - NormalizeGeneratedAt(raw);

        foreach (var match in raw.Matches)
        {
            match.StartTimeUtc = match.StartTimeUtc.Add(drift);
            match.Status = ResolveStatus(match.StartTimeUtc);
        }

        raw.Matches = raw.Matches
            .OrderBy(m => m.Status == "live" ? 0 : 1)
            .ThenBy(m => m.StartTimeUtc)
            .ToList();

        return raw;
    }

    public async Task<List<Sport>> GetSportsAsync()
    {
        var data = await GetFixturesAsync();
        return data.Sports;
    }

    public async Task<List<League>> GetLeaguesAsync()
    {
        var data = await GetFixturesAsync();
        return data.Leagues;
    }

    public async Task<List<Team>> GetTeamsAsync()
    {
        var data = await GetFixturesAsync();
        return data.Teams;
    }

    public async Task<Match?> GetMatchBySlugAsync(string slug)
    {
        var data = await GetFixturesAsync();
        return data.Matches.FirstOrDefault(m =>
            string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    private static DateTime NormalizeGeneratedAt(FixtureData data)
    {
        // Best-effort extraction of the authored reference time so the demo stays
        // aligned with "now". We approximate it from the earliest match plus a
        // small lead so that the earliest events are already live.
        if (data.Matches.Count == 0)
        {
            return DateTime.UtcNow;
        }

        var earliest = data.Matches.Min(m => m.StartTimeUtc);
        return earliest.AddMinutes(30);
    }

    private string ResolveStatus(DateTime startUtc)
    {
        var now = DateTime.UtcNow;
        if (startUtc <= now && startUtc > now - _liveWindow)
        {
            return "live";
        }

        if (startUtc <= now - _liveWindow)
        {
            return "replay";
        }

        return "upcoming";
    }
}
