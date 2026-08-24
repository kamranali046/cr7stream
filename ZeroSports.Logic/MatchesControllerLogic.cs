using ZeroSports.Logic.Models;

namespace ZeroSports.Logic;

public interface IMatchesControllerLogic
{
    Task<SportPageViewModel?> GetBySportAsync(string slug);
    Task<MatchDetailViewModel?> GetMatchAsync(string slug);
}

public class MatchesControllerLogic : IMatchesControllerLogic
{
    private readonly IScrapperLogic _scrapper;

    public MatchesControllerLogic(IScrapperLogic scrapper)
    {
        _scrapper = scrapper;
    }

    public async Task<SportPageViewModel?> GetBySportAsync(string slug)
    {
        var fixtures = await _scrapper.GetFixturesAsync();
        var sport = fixtures.Sports.FirstOrDefault(s =>
            string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));

        if (sport is null)
        {
            return null;
        }

        var hiddenLeagues = new HashSet<string>(
            fixtures.Leagues.Where(l => l.Hidden).Select(l => l.Slug),
            StringComparer.OrdinalIgnoreCase);

        var matches = fixtures.Matches
            .Where(m => m.Enabled
                        && !hiddenLeagues.Contains(m.LeagueSlug)
                        && !string.Equals(m.LeagueSlug, Categories.ImportantMatchesSlug, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(m.SportSlug, slug, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var importantMatches = fixtures.Matches
            .Where(m => m.Enabled
                        && string.Equals(m.SportSlug, slug, StringComparison.OrdinalIgnoreCase)
                        && (m.Important
                            || (string.Equals(m.LeagueSlug, Categories.ImportantMatchesSlug, StringComparison.OrdinalIgnoreCase)
                                && !hiddenLeagues.Contains(m.LeagueSlug))))
            .ToList();

        return new SportPageViewModel
        {
            Sport = sport,
            ImportantMatches = importantMatches,
            Matches = matches
        };
    }

    public async Task<MatchDetailViewModel?> GetMatchAsync(string slug)
    {
        var match = await _scrapper.GetMatchBySlugAsync(slug);
        if (match is null)
        {
            return null;
        }

        var fixtures = await _scrapper.GetFixturesAsync();

        return new MatchDetailViewModel
        {
            Match = match,
            League = fixtures.Leagues.FirstOrDefault(l =>
                string.Equals(l.Slug, match.LeagueSlug, StringComparison.OrdinalIgnoreCase)),
            Sport = fixtures.Sports.FirstOrDefault(s =>
                string.Equals(s.Slug, match.SportSlug, StringComparison.OrdinalIgnoreCase))
        };
    }
}
