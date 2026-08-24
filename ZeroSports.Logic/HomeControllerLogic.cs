using ZeroSports.Logic.Models;

namespace ZeroSports.Logic;

public interface IHomeControllerLogic
{
    Task<HomeViewModel> GetHomeAsync();
}

public class HomeControllerLogic : IHomeControllerLogic
{
    private readonly IScrapperLogic _scrapper;

    public HomeControllerLogic(IScrapperLogic scrapper)
    {
        _scrapper = scrapper;
    }

    public async Task<HomeViewModel> GetHomeAsync()
    {
        var fixtures = await _scrapper.GetFixturesAsync();

        var sections = fixtures.Leagues
            .Select(league => new LeagueSection
            {
                League = league,
                Matches = fixtures.Matches
                    .Where(m => string.Equals(m.LeagueSlug, league.Slug, StringComparison.OrdinalIgnoreCase))
                    .ToList()
            })
            .Where(section => section.Matches.Count > 0)
            .ToList();

        return new HomeViewModel
        {
            Sports = fixtures.Sports,
            TopLeagues = fixtures.Leagues.Take(12).ToList(),
            TopTeams = fixtures.Teams.Take(13).ToList(),
            Sections = sections
        };
    }
}
