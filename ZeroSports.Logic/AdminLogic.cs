using System.Globalization;
using ZeroSports.Logic.Models;
using ZeroSports.Logic.Services;

namespace ZeroSports.Logic;

public class CategoryInput
{
    public string Name { get; set; } = string.Empty;
    public string? Sport { get; set; }
}

public class MatchInput
{
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string? HomeTeamLogo { get; set; }
    public string? AwayTeamLogo { get; set; }
    public DateTime StartTime { get; set; }
    public string Status { get; set; } = "upcoming";
    public string? SourceUrl { get; set; }
}

public interface IAdminLogic
{
    Task<List<League>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<League?> GetCategoryAsync(string slug, CancellationToken cancellationToken = default);
    Task AddCategoryAsync(CategoryInput input, CancellationToken cancellationToken = default);
    Task<bool> DeleteCategoryAsync(string slug, CancellationToken cancellationToken = default);
    Task<Match?> GetMatchAsync(string categorySlug, string matchSlug, CancellationToken cancellationToken = default);
    Task AddMatchAsync(string categorySlug, MatchInput input, CancellationToken cancellationToken = default);
    Task<bool> DeleteMatchAsync(string categorySlug, string matchSlug, CancellationToken cancellationToken = default);
}

public class AdminLogic : IAdminLogic
{
    private readonly IFixtureProvider _provider;

    public AdminLogic(IFixtureProvider provider)
    {
        _provider = provider;
    }

    public async Task<List<League>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        return data.Leagues
            .OrderBy(l => l.Name)
            .ToList();
    }

    public async Task<League?> GetCategoryAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        return data.Leagues.FirstOrDefault(l => l.Slug == slug);
    }

    public async Task AddCategoryAsync(CategoryInput input, CancellationToken cancellationToken = default)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var sportName = string.IsNullOrWhiteSpace(input.Sport) ? name : input.Sport!.Trim();
        var sportSlug = Slug.Slugify(sportName);
        var categorySlug = Slug.Slugify(name);

        var data = await _provider.LoadRawAsync();

        if (data.Leagues.Any(l => l.Slug == categorySlug))
        {
            categorySlug = $"{categorySlug}-{Guid.NewGuid().ToString("N")[..4]}";
        }

        if (!data.Sports.Any(s => s.Slug == sportSlug))
        {
            data.Sports.Add(new Sport
            {
                Slug = sportSlug,
                Name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(sportName.ToLowerInvariant()),
                Logo = $"https://placehold.co/120x120/1f2937/ffffff?text={Uri.EscapeDataString(sportName[..Math.Min(3, sportName.Length)].ToUpper())}"
            });
        }

        data.Leagues.Add(new League
        {
            Slug = categorySlug,
            Name = name,
            SportSlug = sportSlug,
            Logo = string.Empty
        });

        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task<bool> DeleteCategoryAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var removed = data.Leagues.RemoveAll(l => l.Slug == slug);
        if (removed == 0)
        {
            return false;
        }

        data.Matches.RemoveAll(m => m.LeagueSlug == slug);
        await _provider.SaveAsync(data, cancellationToken);
        return true;
    }

    public async Task<Match?> GetMatchAsync(string categorySlug, string matchSlug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        return data.Matches.FirstOrDefault(m =>
            m.LeagueSlug == categorySlug && m.Slug == matchSlug);
    }

    public async Task AddMatchAsync(string categorySlug, MatchInput input, CancellationToken cancellationToken = default)
    {
        var home = (input.HomeTeam ?? "Home").Trim();
        var away = (input.AwayTeam ?? "Away").Trim();
        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
        {
            return;
        }

        var data = await _provider.LoadRawAsync();
        var league = data.Leagues.FirstOrDefault(l => l.Slug == categorySlug);
        if (league is null)
        {
            return;
        }

        var startUtc = DateTime.SpecifyKind(input.StartTime, DateTimeKind.Utc);

        var slug = $"{Slug.Slugify(home)}-vs-{Slug.Slugify(away)}";
        if (data.Matches.Any(m => m.Slug == slug))
        {
            slug = $"{slug}-{Guid.NewGuid().ToString("N")[..4]}";
        }

        var match = new Match
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Slug = slug,
            LeagueSlug = categorySlug,
            SportSlug = league.SportSlug,
            HomeTeam = home,
            AwayTeam = away,
            HomeTeamLogo = input.HomeTeamLogo ?? string.Empty,
            AwayTeamLogo = input.AwayTeamLogo ?? string.Empty,
            StartTimeUtc = startUtc,
            Status = input.Status,
            SourceUrl = input.SourceUrl ?? string.Empty,
            Streams = new List<StreamSource>
            {
                new() { Label = "Watch on source", Url = input.SourceUrl ?? string.Empty }
            }
        };

        data.Matches.Add(match);
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task<bool> DeleteMatchAsync(string categorySlug, string matchSlug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var removed = data.Matches.RemoveAll(m =>
            m.LeagueSlug == categorySlug && m.Slug == matchSlug);
        if (removed == 0)
        {
            return false;
        }

        await _provider.SaveAsync(data, cancellationToken);
        return true;
    }
}
