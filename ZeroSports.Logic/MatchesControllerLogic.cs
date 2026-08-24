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

        var matches = fixtures.Matches
            .Where(m => string.Equals(m.SportSlug, slug, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new SportPageViewModel
        {
            Sport = sport,
            LiveMatches = matches.Where(m => m.Status == "live").ToList(),
            UpcomingMatches = matches.Where(m => m.Status != "live").ToList()
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
