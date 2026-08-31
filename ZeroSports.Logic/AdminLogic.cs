using System.Globalization;
using ZeroSports.Logic.Models;
using ZeroSports.Logic.Scrapers;
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
    Task<Match?> GetMatchBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<ScraperSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(ScraperSettings settings, CancellationToken cancellationToken = default);

    // Category management
    Task ToggleCategoryHiddenAsync(string slug, CancellationToken cancellationToken = default);
    Task MoveCategoryAsync(string slug, string direction, CancellationToken cancellationToken = default);

    // Match management
    Task ToggleImportantAsync(string slug, CancellationToken cancellationToken = default);
    Task ToggleMatchLiveAsync(string slug, CancellationToken cancellationToken = default);
    Task ToggleMatchEndedAsync(string slug, CancellationToken cancellationToken = default);
    Task MoveMatchAsync(string slug, string direction, CancellationToken cancellationToken = default);
    Task RefreshMatchPlayersAsync(string slug, CancellationToken cancellationToken = default);

    // Player (stream) management for a match
    Task AddPlayerAsync(string matchSlug, string name, string url, CancellationToken cancellationToken = default);
    Task TogglePlayerAsync(string matchSlug, int index, CancellationToken cancellationToken = default);
    Task MovePlayerAsync(string matchSlug, int index, string direction, CancellationToken cancellationToken = default);
    Task SavePlayerAsync(string matchSlug, int index, string url, CancellationToken cancellationToken = default);
    Task DeletePlayerAsync(string matchSlug, int index, CancellationToken cancellationToken = default);
}

public class AdminLogic : IAdminLogic
{
    private readonly IFixtureProvider _provider;
    private readonly IScraperSettingsProvider _settings;
    private readonly ITotalSportekScraper _scraper;

    public AdminLogic(IFixtureProvider provider, IScraperSettingsProvider settings, ITotalSportekScraper scraper)
    {
        _provider = provider;
        _settings = settings;
        _scraper = scraper;
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
                Logo = $"https://placehold.co/120x120/1f2937/ffffff?text={Uri.EscapeDataString(sportName[..Math.Min(3, sportName.Length)].ToUpper())}",
                IsCustom = true
            });
        }

        data.Leagues.Add(new League
        {
            Slug = categorySlug,
            Name = name,
            SportSlug = sportSlug,
            Logo = string.Empty,
            IsCustom = true
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
            },
            IsCustom = true
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

    public async Task<Match?> GetMatchBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        return data.Matches.FirstOrDefault(m => m.Slug == slug);
    }

    public async Task ToggleCategoryHiddenAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var league = data.Leagues.FirstOrDefault(l => l.Slug == slug);
        if (league is null)
        {
            return;
        }

        league.Hidden = !league.Hidden;
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task MoveCategoryAsync(string slug, string direction, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var idx = data.Leagues.FindIndex(l => l.Slug == slug);
        if (idx < 0)
        {
            return;
        }

        var swap = direction == "up" ? idx - 1 : idx + 1;
        if (swap < 0 || swap >= data.Leagues.Count)
        {
            return;
        }

        (data.Leagues[idx], data.Leagues[swap]) = (data.Leagues[swap], data.Leagues[idx]);
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task ToggleImportantAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == slug);
        if (match is null)
        {
            return;
        }

        match.Important = !match.Important;
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task ToggleMatchLiveAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == slug);
        if (match is null)
        {
            return;
        }

        match.IsLive = !match.IsLive;
        if (match.IsLive)
        {
            match.IsEnded = false;
        }
        match.LiveStateLocked = true;

        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task ToggleMatchEndedAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == slug);
        if (match is null)
        {
            return;
        }

        match.IsEnded = !match.IsEnded;
        if (match.IsEnded)
        {
            match.IsLive = false;
        }
        match.LiveStateLocked = true;

        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task MoveMatchAsync(string slug, string direction, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var mi = data.Matches.FindIndex(m => m.Slug == slug);
        if (mi < 0)
        {
            return;
        }

        var leagueSlug = data.Matches[mi].LeagueSlug;
        var group = data.Matches
            .Select((m, i) => new { m, i })
            .Where(x => x.m.LeagueSlug == leagueSlug)
            .Select(x => x.i)
            .ToList();

        var pos = group.IndexOf(mi);
        var neighbor = direction == "up" ? pos - 1 : pos + 1;
        if (neighbor < 0 || neighbor >= group.Count)
        {
            return;
        }

        var ni = group[neighbor];
        (data.Matches[mi], data.Matches[ni]) = (data.Matches[ni], data.Matches[mi]);
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task RefreshMatchPlayersAsync(string slug, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == slug);
        if (match is null || string.IsNullOrWhiteSpace(match.SourceUrl))
        {
            return;
        }

        var fetched = await _scraper.GetPlayersAsync(match.SourceUrl, cancellationToken);
        match.Players = MergePlayers(match.Players, fetched);
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task AddPlayerAsync(string matchSlug, string name, string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == matchSlug);
        if (match is null)
        {
            return;
        }

        match.Players ??= new List<Player>();
        var label = string.IsNullOrWhiteSpace(name) ? $"Player {match.Players.Count + 1}" : name.Trim();
        match.Players.Add(new Player
        {
            Name = label,
            Url = url.Trim(),
            Enabled = true,
            IsCustom = true
        });

        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task TogglePlayerAsync(string matchSlug, int index, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == matchSlug);
        if (match?.Players is null || index < 0 || index >= match.Players.Count)
        {
            return;
        }

        match.Players[index].Enabled = !match.Players[index].Enabled;
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task MovePlayerAsync(string matchSlug, int index, string direction, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == matchSlug);
        if (match?.Players is null || index < 0 || index >= match.Players.Count)
        {
            return;
        }

        var swap = direction == "up" ? index - 1 : index + 1;
        if (swap < 0 || swap >= match.Players.Count)
        {
            return;
        }

        (match.Players[index], match.Players[swap]) = (match.Players[swap], match.Players[index]);
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task SavePlayerAsync(string matchSlug, int index, string url, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == matchSlug);
        if (match?.Players is null || index < 0 || index >= match.Players.Count)
        {
            return;
        }

        match.Players[index].Url = url.Trim();
        await _provider.SaveAsync(data, cancellationToken);
    }

    public async Task DeletePlayerAsync(string matchSlug, int index, CancellationToken cancellationToken = default)
    {
        var data = await _provider.LoadRawAsync();
        var match = data.Matches.FirstOrDefault(m => m.Slug == matchSlug);
        if (match?.Players is null || index < 0 || index >= match.Players.Count)
        {
            return;
        }

        match.Players.RemoveAt(index);
        await _provider.SaveAsync(data, cancellationToken);
    }

    private static List<Player> MergePlayers(List<Player> existing, List<Player> fetched)
    {
        existing ??= new List<Player>();
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

    public async Task<ScraperSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _settings.LoadAsync(cancellationToken);
    }

    public async Task SaveSettingsAsync(ScraperSettings settings, CancellationToken cancellationToken = default)
    {
        await _settings.SaveAsync(settings, cancellationToken);
    }
}
