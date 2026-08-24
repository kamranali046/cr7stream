using ZeroSports.Logic.Models;
using ZeroSports.Logic.Scrapers;
using ZeroSports.Logic.Services;

namespace ZeroSports.Logic;

public interface IScrapperLogic
{
    Task<FixtureData> GetFixturesAsync();
    Task<FixtureData> ScrapeAndSaveAsync(CancellationToken cancellationToken = default);
    Task<List<Sport>> GetSportsAsync();
    Task<List<League>> GetLeaguesAsync();
    Task<List<Team>> GetTeamsAsync();
    Task<Match?> GetMatchBySlugAsync(string slug);
}

public class ScrapperLogic : IScrapperLogic
{
    private readonly IFixtureProvider _provider;
    private readonly ITotalSportekScraper _scraper;
    private readonly TimeSpan _liveWindow = TimeSpan.FromHours(3);

    public ScrapperLogic(IFixtureProvider provider, ITotalSportekScraper scraper)
    {
        _provider = provider;
        _scraper = scraper;
    }

    public async Task<FixtureData> GetFixturesAsync()
    {
        var raw = await _provider.LoadRawAsync();

        // Dummy data is anchored to a fixed reference time; rebase it relative to
        // "now" so the demo always shows live events + countdowns. Scraped data
        // already carries real absolute timestamps and skips this step.
        if (raw.NormalizeTimes)
        {
            var drift = DateTime.UtcNow - NormalizeGeneratedAt(raw);
            foreach (var match in raw.Matches)
            {
                match.StartTimeUtc = match.StartTimeUtc.Add(drift);
                match.Status = ResolveStatus(match.StartTimeUtc);
            }
        }

        // Preserve the stored order so manual re-ordering (MoveMatch / MoveCategory
        // in the admin panel) is reflected on the public site exactly as configured.
        return raw;
    }

    public async Task<FixtureData> ScrapeAndSaveAsync(CancellationToken cancellationToken = default)
    {
        var data = await _scraper.ScrapeAsync(cancellationToken);
        data.NormalizeTimes = false;
        data.ScrapedAtUtc = DateTime.UtcNow;

        var existing = await _provider.LoadRawAsync();
        if (existing.Leagues.Count != 0 || existing.Matches.Count != 0)
        {
            MergePreserveCustom(data, existing);
        }

        await _provider.SaveAsync(data, cancellationToken);
        return data;
    }

    /// <summary>
    /// Replaces scraped content but keeps admin overrides (hidden categories,
    /// match enable/important/live/ended flags, custom leagues/matches and any
    /// custom player edits) so an auto-scrape never destroys manual changes.
    /// Matches are correlated by <see cref="Match.Slug"/> (stable across scrapes)
    /// because freshly scraped matches get new random Ids each run.
    /// </summary>
    private static void MergePreserveCustom(FixtureData scraped, FixtureData existing)
    {
        var scrapedSportSlugs = new HashSet<string>(
            scraped.Sports.Select(s => s.Slug), StringComparer.OrdinalIgnoreCase);
        foreach (var sport in existing.Sports.Where(s => s.IsCustom && !scrapedSportSlugs.Contains(s.Slug)))
        {
            scraped.Sports.Add(sport);
        }

        var scrapedLeagueSlugs = new HashSet<string>(
            scraped.Leagues.Select(l => l.Slug), StringComparer.OrdinalIgnoreCase);
        foreach (var league in scraped.Leagues)
        {
            var prior = existing.Leagues.FirstOrDefault(l => l.Slug == league.Slug);
            if (prior is not null)
            {
                league.Hidden = prior.Hidden;
                league.IsCustom = prior.IsCustom;
            }
        }
        foreach (var league in existing.Leagues.Where(l => l.IsCustom && !scrapedLeagueSlugs.Contains(l.Slug)))
        {
            scraped.Leagues.Add(league);
        }

        foreach (var match in scraped.Matches)
        {
            var prior = existing.Matches.FirstOrDefault(m => m.Slug == match.Slug);
            if (prior is null)
            {
                continue;
            }

            match.Enabled = prior.Enabled;
            match.Important = prior.Important;
            match.IsLive = prior.IsLive;
            match.IsEnded = prior.IsEnded;
            match.IsCustom = prior.IsCustom;
            match.Players = MergePlayers(prior.Players, match.Players);
        }

        var scrapedMatchSlugs = new HashSet<string>(
            scraped.Matches.Select(m => m.Slug), StringComparer.OrdinalIgnoreCase);
        foreach (var match in existing.Matches.Where(m => m.IsCustom && !scrapedMatchSlugs.Contains(m.Slug)))
        {
            scraped.Matches.Add(match);
        }
    }

    /// <summary>
    /// Keeps admin-added players and preserves enable/disable state for players
    /// that still exist on the source, while appending any genuinely new players
    /// the scrape discovered. Existing order is preserved so manual re-ordering
    /// of players survives a re-scrape.
    /// </summary>
    private static List<Player> MergePlayers(List<Player> existing, List<Player> fetched)
    {
        var result = new List<Player>();

        foreach (var player in existing)
        {
            if (player.IsCustom)
            {
                result.Add(player);
                continue;
            }

            var match = fetched.FirstOrDefault(f => string.Equals(f.Url, player.Url, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                match.Enabled = player.Enabled;
                result.Add(match);
            }
        }

        foreach (var player in fetched)
        {
            if (!result.Any(r => string.Equals(r.Url, player.Url, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(player);
            }
        }

        return result;
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
