using System.Collections.Concurrent;
using cr7stream.Logic.Models;
using cr7stream.Logic.Scrapers;
using cr7stream.Logic.Services;

namespace cr7stream.Logic;

    public interface IMatchesControllerLogic
    {
        Task<SportPageViewModel?> GetBySportAsync(string slug);
        Task<TeamPageViewModel?> GetByTeamAsync(string slug);
        Task<MatchDetailViewModel?> GetMatchAsync(string slug);
        Task<List<Player>?> GetMatchPlayersAsync(string slug);
    }

public class MatchesControllerLogic : IMatchesControllerLogic
{
    private readonly IScrapperLogic _scrapper;
    private readonly ITotalSportekScraper _scraper;
    private readonly IScraperSettingsProvider _settings;

    // Short-lived cache so repeated views of the same match don't re-drill every time.
    private static readonly ConcurrentDictionary<string, (DateTime Expiry, List<Player> Players)> PlayerCache = new();

    public static void ClearPlayerCache(string? url = null)
    {
        if (url is not null)
        {
            PlayerCache.TryRemove(url, out _);
        }
        else
        {
            PlayerCache.Clear();
        }
    }

    public MatchesControllerLogic(IScrapperLogic scrapper, ITotalSportekScraper scraper, IScraperSettingsProvider settings)
    {
        _scrapper = scrapper;
        _scraper = scraper;
        _settings = settings;
    }

    private static readonly Dictionary<string, string> KnownSportNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["football"] = "Football",
            ["basketball"] = "Basketball",
            ["nfl"] = "American Football",
            ["motorsport"] = "Motorsport",
            ["wwe"] = "WWE",
        };

    public async Task<SportPageViewModel?> GetBySportAsync(string slug)
    {
        var fixtures = await _scrapper.GetFixturesAsync();
        var sport = fixtures.Sports.FirstOrDefault(s =>
            string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));

        // Unknown slug (e.g. a curated category not yet scraped): render a
        // placeholder page instead of 404 so the navbar links never dead-end.
        var displaySport = sport ?? new Sport
        {
            Slug = slug,
            Name = KnownSportNames.TryGetValue(slug, out var name)
                ? name
                : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug)
        };

        var hiddenLeagues = new HashSet<string>(
            fixtures.Leagues.Where(l => l.Hidden).Select(l => l.Slug),
            StringComparer.OrdinalIgnoreCase);

        var matches = fixtures.Matches
            .Where(m => !hiddenLeagues.Contains(m.LeagueSlug)
                        && !string.Equals(m.LeagueSlug, Categories.ImportantMatchesSlug, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(m.SportSlug, slug, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var importantMatches = fixtures.Matches
            .Where(m => string.Equals(m.SportSlug, slug, StringComparison.OrdinalIgnoreCase)
                        && (m.Important
                            || (string.Equals(m.LeagueSlug, Categories.ImportantMatchesSlug, StringComparison.OrdinalIgnoreCase)
                                && !hiddenLeagues.Contains(m.LeagueSlug))))
            .ToList();

        return new SportPageViewModel
        {
            Sport = displaySport,
            ImportantMatches = importantMatches,
            Matches = matches
        };
    }

    public async Task<TeamPageViewModel?> GetByTeamAsync(string slug)
    {
        var fixtures = await _scrapper.GetFixturesAsync();
        var team = fixtures.Teams.FirstOrDefault(t =>
            string.Equals(t.Slug, slug, StringComparison.OrdinalIgnoreCase));

        // Unknown team slug: render a placeholder page instead of 404.
        var displayTeam = team ?? new Team
        {
            Slug = slug,
            Name = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                slug.Replace('-', ' '))
        };

        var hiddenLeagues = new HashSet<string>(
            fixtures.Leagues.Where(l => l.Hidden).Select(l => l.Slug),
            StringComparer.OrdinalIgnoreCase);

        var matches = fixtures.Matches
            .Where(m => !hiddenLeagues.Contains(m.LeagueSlug)
                        && (string.Equals(m.HomeTeam, displayTeam.Name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(m.AwayTeam, displayTeam.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new TeamPageViewModel
        {
            Team = displayTeam,
            Matches = matches
        };
    }

    public async Task<MatchDetailViewModel?> GetMatchAsync(string slug)
    {
        var fixtures = await _scrapper.GetFixturesAsync();
        var match = fixtures.Matches.FirstOrDefault(m =>
            string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return null;
        }

        var settings = await _settings.LoadAsync();

        var isEnded = match.IsEnded || match.Status == "replay";
        var isLive = match.IsLive && !isEnded;
        var lead = settings.PlayerFetchLeadMinutes;
        // Players only exist once the source publishes them, which happens roughly
        // within the configured lead window before kickoff. Live matches always have
        // them; ended matches show the ended notice instead.
        var playersAvailable = isLive
                               || isEnded
                               || DateTime.UtcNow >= match.StartTimeUtc.AddMinutes(-lead);

        // NOTE: players are intentionally NOT resolved here. Doing so would block the
        // page render for several seconds while we drill provider pages. Instead the
        // player screen loads instantly and fetches players via GetMatchPlayersAsync
        // (called by the /hd/{slug}/players endpoint) which caches the result.
        return new MatchDetailViewModel
        {
            Match = match,
            League = fixtures.Leagues.FirstOrDefault(l =>
                string.Equals(l.Slug, match.LeagueSlug, StringComparison.OrdinalIgnoreCase)),
            Sport = fixtures.Sports.FirstOrDefault(s =>
                string.Equals(s.Slug, match.SportSlug, StringComparison.OrdinalIgnoreCase)),
            PlayersAvailable = playersAvailable,
            PlayerLeadMinutes = lead
        };
    }

    public async Task<List<Player>?> GetMatchPlayersAsync(string slug)
    {
        var match = await _scrapper.GetMatchBySlugAsync(slug);
        if (match is null)
        {
            return null;
        }

        var stored = match.Players ?? new List<Player>();

        if (string.IsNullOrWhiteSpace(match.SourceUrl))
        {
            return stored.Count > 0 ? stored : null;
        }

        var fresh = await GetCachedPlayersAsync(match.SourceUrl, System.Threading.CancellationToken.None);
        if (fresh.Count == 0)
        {
            // Source has no players yet — return manually added ones if any.
            return stored.Count > 0 ? stored : fresh;
        }

        return MergeWithStored(stored, fresh);
    }

    private async Task<List<Player>> GetCachedPlayersAsync(string url, System.Threading.CancellationToken ct)
    {
        if (PlayerCache.TryGetValue(url, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return cached.Players;
        }

        var players = await _scraper.GetPlayersAsync(url, ct);
        PlayerCache[url] = (DateTime.UtcNow.AddMinutes(10), players);
        return players;
    }

    /// <summary>
    /// Keeps admin-added/custom players and preserves the enabled/disabled state of
    /// players that still exist on the source, then appends any genuinely new players
    /// the live fetch discovered. Fresh order wins so new streams appear first.
    /// </summary>
    private static List<Player> MergeWithStored(List<Player> existing, List<Player> fetched)
    {
        var result = new List<Player>();
        foreach (var player in existing)
        {
            if (player.IsCustom)
            {
                result.Add(player);
            }
        }

        foreach (var player in fetched)
        {
            var prior = existing.FirstOrDefault(e =>
                string.Equals(e.Url, player.Url, StringComparison.OrdinalIgnoreCase));
            if (prior is not null)
            {
                player.Enabled = prior.Enabled;
            }
            result.Add(player);
        }

        return result;
    }
}

