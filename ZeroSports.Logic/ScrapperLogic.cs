using System.Collections.Concurrent;
using ZeroSports.Logic.Models;
using ZeroSports.Logic.Scrapers;
using ZeroSports.Logic.Services;

namespace ZeroSports.Logic;

public interface IScrapperLogic
{
    Task<FixtureData> GetFixturesAsync();
    Task<FixtureData> ScrapeAndSaveAsync(CancellationToken cancellationToken = default, bool drillPlayers = false);
    Task<FixtureData> ScrapeFixturesAndDrillPlayersAsync(CancellationToken cancellationToken = default);
    Task SaveFixturesAsync(FixtureData data, CancellationToken cancellationToken = default);
    Task ProcessLiveWindowsAsync(CancellationToken cancellationToken = default);
    Task<List<Sport>> GetSportsAsync();
    Task<List<League>> GetLeaguesAsync();
    Task<List<Team>> GetTeamsAsync();
    Task<Match?> GetMatchBySlugAsync(string slug);
}

public class ScrapperLogic : IScrapperLogic
{
    private readonly IFixtureProvider _provider;
    private readonly ITotalSportekScraper _scraper;
    private readonly IScraperSettingsProvider _settings;
    private readonly ILogoService _logos;
    private readonly TimeSpan _liveWindow = TimeSpan.FromHours(3);

    // Throttles per-match player re-drills so the live-window loop (which runs
    // every couple of minutes) doesn't hammer the source for a match that hasn't
    // published players yet.
    private static readonly ConcurrentDictionary<string, DateTime> LastDrill = new();

    public ScrapperLogic(IFixtureProvider provider, ITotalSportekScraper scraper, IScraperSettingsProvider settings, ILogoService logos)
    {
        _provider = provider;
        _scraper = scraper;
        _settings = settings;
        _logos = logos;
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

    public async Task<FixtureData> ScrapeAndSaveAsync(CancellationToken cancellationToken = default, bool drillPlayers = false)
    {
        var data = await _scraper.ScrapeAsync(cancellationToken, drillPlayers);
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
    /// Two-phase scrape:
    ///   Phase 1 — scrape fixtures (teams, time, source URL, status), save immediately.
    ///   Phase 2 — drill players for live matches or those within PlayerFetchLeadMinutes.
    /// Also downloads team/league logos to local disk for caching.
    /// Returns data after Phase 1 so the caller can show fixtures right away while
    /// Phase 2 runs in the background.
    /// </summary>
    public async Task<FixtureData> ScrapeFixturesAndDrillPlayersAsync(CancellationToken cancellationToken = default)
    {
        // Phase 1: fixtures only (fast, no network calls for player pages)
        var data = await _scraper.ScrapeFixturesAsync(cancellationToken);
        data.NormalizeTimes = false;
        data.ScrapedAtUtc = DateTime.UtcNow;

        var existing = await _provider.LoadRawAsync();
        if (existing.Leagues.Count != 0 || existing.Matches.Count != 0)
        {
            MergePreserveCustom(data, existing);
        }

        // Save fixtures immediately so the admin page can display them.
        await _provider.SaveAsync(data, cancellationToken);

        // Download logos in background (fire-and-forget).
        _ = Task.Run(async () =>
        {
            try
            {
                await DownloadLogosAsync(data, cancellationToken);
            }
            catch
            {
                // best-effort
            }
        }, cancellationToken);

        // Phase 2: drill players for live / near-live matches (fire-and-forget).
        _ = Task.Run(async () =>
        {
            try
            {
                await _scraper.DrillPlayersAsync(data, cancellationToken);
                await _provider.SaveAsync(data, cancellationToken);
                MatchesControllerLogic.ClearPlayerCache();
            }
            catch
            {
                // best-effort: player drilling failure must not crash the app
            }
        }, cancellationToken);

        return data;
    }

    public async Task SaveFixturesAsync(FixtureData data, CancellationToken cancellationToken = default)
    {
        await _provider.SaveAsync(data, cancellationToken);
    }

    /// <summary>
    /// Downloads team and league logos to local disk for caching.
    /// Skips already-downloaded logos. Updates the data in-place with local paths.
    /// </summary>
    public async Task DownloadLogosAsync(FixtureData data, CancellationToken ct = default)
    {
        var tasks = new List<Task>();

        foreach (var team in data.Teams)
        {
            if (!string.IsNullOrWhiteSpace(team.Logo) && !_logos.LocalFileExists(team.Slug))
            {
                var logo = team.Logo;
                var slug = team.Slug;
                tasks.Add(Task.Run(async () =>
                {
                    var local = await _logos.GetOrDownloadAsync(logo, slug, ct);
                    if (local != logo)
                    {
                        team.Logo = local;
                    }
                }, ct));
            }
            else if (_logos.LocalFileExists(team.Slug))
            {
                team.Logo = _logos.GetLocalPath(team.Slug);
            }
        }

        foreach (var league in data.Leagues)
        {
            if (!string.IsNullOrWhiteSpace(league.Logo) && !_logos.LocalFileExists(league.Slug))
            {
                var logo = league.Logo;
                var slug = league.Slug;
                tasks.Add(Task.Run(async () =>
                {
                    var local = await _logos.GetOrDownloadAsync(logo, slug, ct);
                    if (local != logo)
                    {
                        league.Logo = local;
                    }
                }, ct));
            }
            else if (_logos.LocalFileExists(league.Slug))
            {
                league.Logo = _logos.GetLocalPath(league.Slug);
            }
        }

        // Also fix match-level logos (home/away team logos stored per-match)
        foreach (var match in data.Matches)
        {
            if (!string.IsNullOrWhiteSpace(match.HomeTeamLogo) && !_logos.LocalFileExists(Slug.Slugify(match.HomeTeam)))
            {
                var logo = match.HomeTeamLogo;
                var slug = Slug.Slugify(match.HomeTeam);
                tasks.Add(Task.Run(async () =>
                {
                    var local = await _logos.GetOrDownloadAsync(logo, slug, ct);
                    if (local != logo)
                    {
                        match.HomeTeamLogo = local;
                    }
                }, ct));
            }
            else if (_logos.LocalFileExists(Slug.Slugify(match.HomeTeam)))
            {
                match.HomeTeamLogo = _logos.GetLocalPath(Slug.Slugify(match.HomeTeam));
            }

            if (!string.IsNullOrWhiteSpace(match.AwayTeamLogo) && !_logos.LocalFileExists(Slug.Slugify(match.AwayTeam)))
            {
                var logo = match.AwayTeamLogo;
                var slug = Slug.Slugify(match.AwayTeam);
                tasks.Add(Task.Run(async () =>
                {
                    var local = await _logos.GetOrDownloadAsync(logo, slug, ct);
                    if (local != logo)
                    {
                        match.AwayTeamLogo = local;
                    }
                }, ct));
            }
            else if (_logos.LocalFileExists(Slug.Slugify(match.AwayTeam)))
            {
                match.AwayTeamLogo = _logos.GetLocalPath(Slug.Slugify(match.AwayTeam));
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Background monitor that, between daily scrapes, keeps live/ended state and
    /// player lists fresh using the configured lead windows:
    ///   - drills a match's players ~PlayerFetchLeadMinutes before kickoff,
    ///   - flags the match LIVE ~LiveMarkLeadMinutes before kickoff,
    ///   - re-drills from source immediately after going live if players are missing,
    ///   - auto-ends a match that has been live longer than LiveAutoEndHours.
    /// Admin-overridden matches (LiveStateLocked) are skipped so manual choices win.
    /// </summary>
    public async Task ProcessLiveWindowsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        var drillLead = TimeSpan.FromMinutes(settings.PlayerFetchLeadMinutes);
        var liveLead = TimeSpan.FromMinutes(settings.LiveMarkLeadMinutes);
        var autoEnd = TimeSpan.FromHours(settings.LiveAutoEndHours);
        var now = DateTime.UtcNow;

        // Evict LastDrill entries older than auto-end window to prevent memory leak
        var evictionCutoff = now - autoEnd;
        foreach (var key in LastDrill.Keys)
        {
            if (LastDrill.TryGetValue(key, out var ts) && ts < evictionCutoff)
            {
                LastDrill.TryRemove(key, out _);
            }
        }

        var data = await GetFixturesAsync();
        var changed = false;

        foreach (var match in data.Matches)
        {
            try
            {
                if (match.LiveStateLocked)
                {
                    continue;
                }

                var isEnded = match.IsEnded || match.Status == "replay";
                if (isEnded)
                {
                    continue;
                }

                // Auto-end a live match that has run past the configured cap.
                if (match.IsLive && now > match.StartTimeUtc.Add(autoEnd))
                {
                    match.IsLive = false;
                    match.IsEnded = true;
                    match.Status = "replay";
                    changed = true;
                    continue;
                }

                var timeToStart = match.StartTimeUtc - now;

                // Active window: from the drill lead up until roughly the auto-end cap
                // after kickoff. Outside this we leave the match alone.
                var inWindow = timeToStart <= drillLead && timeToStart > -autoEnd;
                if (inWindow && !HasEnabledPlayers(match))
                {
                    changed |= await TryDrillPlayersAsync(match, now, drillLead, cancellationToken);
                }

                if (timeToStart <= liveLead)
                {
                    if (!match.IsLive)
                    {
                        match.IsLive = true;
                        match.Status = "live";
                        changed = true;
                    }

                    // Just went live (or already live) but no players yet: pull again
                    // from the source right away.
                    if (!HasEnabledPlayers(match))
                    {
                        changed |= await TryDrillPlayersAsync(match, now, liveLead, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                // Per-match failure must not abort the entire loop.
                System.Diagnostics.Debug.WriteLine($"Live-window processing failed for {match.Slug}: {ex.Message}");
            }
        }

        if (changed)
        {
            await _provider.SaveAsync(data, cancellationToken);
            MatchesControllerLogic.ClearPlayerCache();
        }
    }

    private static bool HasEnabledPlayers(Match match)
    {
        return match.Players is { Count: > 0 } && match.Players.Any(p => p.Enabled);
    }

    private async Task<bool> TryDrillPlayersAsync(Match match, DateTime now, TimeSpan minInterval, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(match.SourceUrl))
        {
            return false;
        }

        // Don't re-drill more often than the smaller of the two leads so a match
        // whose source isn't ready yet is retried at a sane pace.
        if (LastDrill.TryGetValue(match.Slug, out var last) && now - last < minInterval)
        {
            return false;
        }

        LastDrill[match.Slug] = now;

        var fresh = await _scraper.GetPlayersAsync(match.SourceUrl, ct);
        if (fresh.Count == 0)
        {
            return false;
        }

        match.Players = MergePlayers(match.Players, fresh);
        return true;
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

            match.Important = prior.Important;
            match.IsCustom = prior.IsCustom;

            // Live/ended are synced from the source status for normal matches, but
            // once the admin manually overrides them (LiveStateLocked) the scraper
            // must leave them alone so the dashboard's choice actually sticks.
            if (prior.IsCustom || prior.LiveStateLocked)
            {
                match.IsLive = prior.IsLive;
                match.IsEnded = prior.IsEnded;
            }
            else
            {
                match.IsLive = match.Status == "live";
                match.IsEnded = match.Status == "replay";
            }
            match.LiveStateLocked = prior.LiveStateLocked;

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
        return PlayerMergeHelper.MergePlayers(existing, fetched);
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
