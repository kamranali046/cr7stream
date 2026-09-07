using cr7stream.Logic.Models;
using Microsoft.Extensions.Caching.Memory;

namespace cr7stream.Logic;

public interface IHomeControllerLogic
{
    Task<HomeViewModel> GetHomeAsync();
}

public class HomeControllerLogic : IHomeControllerLogic
{
    private readonly IScrapperLogic _scrapper;
    private readonly IMemoryCache _memoryCache;
    private const string HomeCacheKey = "HomeViewModel";

    public HomeControllerLogic(IScrapperLogic scrapper, IMemoryCache memoryCache)
    {
        _scrapper = scrapper;
        _memoryCache = memoryCache;
    }

    public async Task<HomeViewModel> GetHomeAsync()
    {
        if (_memoryCache.TryGetValue(HomeCacheKey, out HomeViewModel? cached) && cached is not null)
        {
            return cached;
        }

        var fixtures = await _scrapper.GetFixturesAsync();

        var hiddenLeagues = new HashSet<string>(
            fixtures.Leagues.Where(l => l.Hidden).Select(l => l.Slug),
            StringComparer.OrdinalIgnoreCase);

        var visibleLeagues = fixtures.Leagues
            .Where(l => !l.Hidden)
            .ToList();

        // Virtual "Important Matches" section: every enabled match the admin
        // starred, plus any match the source placed in its own featured/important
        // area (the renamed "important-matches" league). Admin-starred matches
        // always show here, even if their real league is hidden.
        var importantMatches = fixtures.Matches
            .Where(m => m.Important
                        || (string.Equals(m.LeagueSlug, Categories.ImportantMatchesSlug, StringComparison.OrdinalIgnoreCase)
                            && !hiddenLeagues.Contains(m.LeagueSlug)))
            .ToList();

        var sections = visibleLeagues
            .Where(l => !string.Equals(l.Slug, Categories.ImportantMatchesSlug, StringComparison.OrdinalIgnoreCase))
            .Select(league => new LeagueSection
            {
                League = league,
                Matches = fixtures.Matches
                    .Where(m => string.Equals(m.LeagueSlug, league.Slug, StringComparison.OrdinalIgnoreCase))
                    .ToList()
            })
            .Where(section => section.Matches.Count > 0)
            .ToList();

        var model = new HomeViewModel
        {
            Sports = fixtures.Sports,
            TopLeagues = visibleLeagues.Take(12).ToList(),
            TopTeams = fixtures.Teams.Take(13).ToList(),
            ImportantMatches = importantMatches,
            Sections = sections
        };

        _memoryCache.Set(HomeCacheKey, model, TimeSpan.FromSeconds(30));
        return model;
    }
}

