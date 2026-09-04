using Microsoft.AspNetCore.Mvc;
using cr7stream.Logic;

namespace cr7stream.Controllers;

public class SearchController : Controller
{
    private readonly IScrapperLogic _scrapper;

    public SearchController(IScrapperLogic scrapper)
    {
        _scrapper = scrapper;
    }

    [HttpGet]
    public async Task<IActionResult> Find(string q)
    {
        ViewData["Query"] = q;
        var fixtures = await _scrapper.GetFixturesAsync();
        var leagueNames = fixtures.Leagues
            .ToDictionary(l => l.Slug, l => l.Name, StringComparer.OrdinalIgnoreCase);

        var query = (q ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return View(new List<cr7stream.Logic.Models.Match>());
        }

        var matches = fixtures.Matches
            .Where(m =>
                (m.HomeTeam != null && m.HomeTeam.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (m.AwayTeam != null && m.AwayTeam.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (m.LeagueSlug != null && leagueNames.TryGetValue(m.LeagueSlug, out var name) && name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return View(matches);
    }
}

