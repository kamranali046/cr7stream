using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ZeroSports.Logic.Models;
using ZeroSports.Logic.Services;

namespace ZeroSports.Logic.Scrapers;

public interface ITotalSportekScraper
{
    Task<FixtureData> ScrapeAsync(CancellationToken cancellationToken = default);
    Task<List<Player>> GetPlayersAsync(string matchUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Messy scraping/parsing logic. Downloads a source homepage HTML and extracts
/// categories -> matches (teams, time, link). The source URL comes from the
/// scraper settings so it is editable from the admin panel.
/// This is intentionally full of heuristics so the controller-facing logic stays clean.
/// </summary>
public class TotalSportekScraper : ITotalSportekScraper
{
    private static readonly Dictionary<string, (string Slug, string Name)> SportMap = new()
    {
        { "football", ("football", "Football") },
        { "soccer", ("football", "Football") },
        { "premier", ("football", "Football") },
        { "la-liga", ("football", "Football") },
        { "liga", ("football", "Football") },
        { "ligue", ("football", "Football") },
        { "serie", ("football", "Football") },
        { "bundesliga", ("football", "Football") },
        { "superlig", ("football", "Football") },
        { "saudi", ("football", "Football") },
        { "mls", ("football", "Football") },
        { "eredivisie", ("football", "Football") },
        { "pokal", ("football", "Football") },
        { "cup", ("football", "Football") },
        { "championship", ("football", "Football") },
        { "cricket", ("cricket", "Cricket") },
        { "tennis", ("tennis", "Tennis") },
        { "wwe", ("wwe", "WWE") },
        { "wrestl", ("wwe", "WWE") },
        { "basketball", ("basketball", "Basketball") },
        { "nba", ("basketball", "Basketball") },
        { "wnba", ("basketball", "Basketball") },
        { "nfl", ("nfl", "NFL") },
        { "mlb", ("baseball", "Baseball") },
        { "baseball", ("baseball", "Baseball") },
        { "rugby", ("rugby", "Rugby") },
        { "boxing", ("boxing", "Boxing") },
        { "ufc", ("ufc", "UFC") },
        { "f1", ("motorsport", "Motorsport") },
        { "formula", ("motorsport", "Motorsport") },
        { "motogp", ("motorsport", "Motorsport") },
        { "motor", ("motorsport", "Motorsport") },
        { "hockey", ("hockey", "Hockey") },
    };

    private readonly HttpClient _http;
    private readonly IScraperSettingsProvider? _settings;
    private string _baseUrl = "https://total-sportek.st/";

    public TotalSportekScraper(HttpClient http, IScraperSettingsProvider? settings = null)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<FixtureData> ScrapeAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings is null
            ? new ScraperSettings()
            : await _settings.LoadAsync(cancellationToken);
        _baseUrl = NormalizeBaseUrl(settings.SourceUrl);

        var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var data = new FixtureData { NormalizeTimes = false, ScrapedAtUtc = DateTime.UtcNow };

        var leagues = new Dictionary<string, League>();
        var sports = new Dictionary<string, Sport>();
        var teams = new Dictionary<string, Team>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<Models.Match>();

        string currentCategory = Categories.ImportantMatchesName;
        string currentCategorySlug = Categories.ImportantMatchesSlug;
        string currentSportSlug = Categories.ImportantMatchesSlug;
        string currentCategoryLogo = string.Empty;

        var nodes = doc.DocumentNode.SelectNodes("//div | //a") ?? Enumerable.Empty<HtmlNode>();

        // Lazily registers a league (and its parent sport) so matches that appear
        // before any category header (e.g. the "Important Games" section) still get
        // a valid league/sport entry. Returns the resolved sport slug.
        string EnsureLeague(string slug, string name, string logo)
        {
            if (!leagues.ContainsKey(slug))
            {
                var (sportSlug, sportName) = NormalizeSport(name);
                leagues[slug] = new League
                {
                    Slug = slug,
                    Name = name,
                    SportSlug = sportSlug,
                    Logo = logo
                };

                if (!sports.ContainsKey(sportSlug))
                {
                    sports[sportSlug] = new Sport
                    {
                        Slug = sportSlug,
                        Name = sportName,
                        Logo = string.IsNullOrEmpty(logo)
                            ? $"https://placehold.co/120x120/1f2937/ffffff?text={Uri.EscapeDataString(sportName[..Math.Min(3, sportName.Length)].ToUpper())}"
                            : logo
                    };
                }

                return sportSlug;
            }

            return leagues[slug].SportSlug;
        }

        foreach (var node in nodes)
        {
            var cls = node.GetAttributeValue("class", "");

            if (IsCategoryHeader(cls))
            {
                // Real categories carry a league logo image; label-only headers
                // such as "Important Games" should keep the previous category.
                if (node.SelectSingleNode(".//img") is null)
                {
                    continue;
                }

                var (name, logo) = ParseCategory(node);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                currentCategory = name.Trim();
                currentCategorySlug = Slug.Slugify(currentCategory);
                currentCategoryLogo = logo;
                currentSportSlug = EnsureLeague(currentCategorySlug, currentCategory, logo);

                continue;
            }

            if (IsMatchAnchor(cls))
            {
                currentSportSlug = EnsureLeague(currentCategorySlug, currentCategory, currentCategoryLogo);
                var match = ParseMatch(node, currentCategorySlug, currentSportSlug, teams);
                if (match is not null)
                {
                    matches.Add(match);
                }
            }
        }

        data.Leagues = leagues.Values.ToList();
        data.Sports = sports.Values.ToList();
        data.Teams = teams.Values.ToList();
        data.Matches = matches;

        // Pull the player/stream list for matches that are live or close to
        // kickoff (total-sportek publishes players ~1h before start).
        var lead = TimeSpan.FromMinutes(settings.PlayerFetchLeadMinutes);
        var now = DateTime.UtcNow;
        foreach (var match in matches)
        {
            match.Players = new List<Player>();
            var beforeStart = match.StartTimeUtc - now;
            var eligible = match.Status == "live"
                           || (beforeStart <= lead && beforeStart > TimeSpan.FromHours(-3));
            if (eligible && !string.IsNullOrWhiteSpace(match.SourceUrl))
            {
                match.Players = await ExtractPlayersAsync(match.SourceUrl, cancellationToken);
            }
        }

        return data;
    }

    public async Task<List<Player>> GetPlayersAsync(string matchUrl, CancellationToken cancellationToken = default)
    {
        return await ExtractPlayersAsync(matchUrl, cancellationToken);
    }

    private async Task<List<Player>> ExtractPlayersAsync(string url, CancellationToken cancellationToken)
    {
        var players = new List<Player>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return players;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, MakeAbsolute(url));
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return players;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var baseHost = SafeHost(_baseUrl);

            foreach (var iframe in doc.DocumentNode.SelectNodes("//iframe") ?? Enumerable.Empty<HtmlNode>())
            {
                AddPlayer(players, seen, iframe.GetAttributeValue("src", ""), baseHost);
            }

            foreach (var a in doc.DocumentNode.SelectNodes("//a") ?? Enumerable.Empty<HtmlNode>())
            {
                AddPlayer(players, seen, a.GetAttributeValue("href", ""), baseHost);
            }
        }
        catch
        {
            // best-effort: a single failed match page must not break the scrape
        }

        return players;
    }

    private void AddPlayer(List<Player> players, HashSet<string> seen, string raw, string? baseHost)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var url = MakeAbsolute(raw);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (baseHost is not null && string.Equals(uri.Host, baseHost, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!LooksLikePlayer(uri))
        {
            return;
        }

        if (!seen.Add(uri.AbsoluteUri))
        {
            return;
        }

        players.Add(new Player
        {
            Name = $"Player {players.Count + 1}",
            Url = uri.AbsoluteUri,
            Enabled = true
        });
    }

    private static bool LooksLikePlayer(Uri uri)
    {
        var h = uri.Host.ToLowerInvariant();
        var p = uri.PathAndQuery.ToLowerInvariant();
        if (h.Contains("google.") || h.Contains("facebook.") || h.Contains("twitter.") || h.Contains("instagram.") || h.Contains("t.co"))
        {
            return false;
        }

        return p.Contains("embed")
            || p.Contains("player")
            || p.Contains("stream")
            || p.Contains("watch")
            || p.Contains("play")
            || p.Contains(".m3u8")
            || p.Contains("iframe")
            || p.Contains("live")
            || h.Contains("youtu")
            || h.Contains("dai.ly")
            || h.Contains("ok.ru")
            || h.Contains("player")
            || h.Contains("embed");
    }

    private static string? SafeHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : null;
    }

    private static bool IsCategoryHeader(string cls)
    {
        if (string.IsNullOrEmpty(cls)) return false;
        return cls.Contains("text-white") && cls.Contains("fw-bold") && cls.Contains("m-2");
    }

    private static bool IsMatchAnchor(string cls)
    {
        return !string.IsNullOrEmpty(cls) && cls.Contains("nav-link2");
    }

    private (string Name, string Logo) ParseCategory(HtmlNode node)
    {
        var img = node.SelectSingleNode(".//img");
        var name = node.SelectSingleNode(".//span")?.InnerText.Trim()
                   ?? img?.GetAttributeValue("alt", "")?.Trim()
                   ?? string.Empty;

        var logo = img is null ? string.Empty : MakeAbsolute(img.GetAttributeValue("src", ""));
        return (name, logo);
    }

    private Models.Match? ParseMatch(HtmlNode anchor, string leagueSlug, string sportSlug, Dictionary<string, Team> teams)
    {
        var href = anchor.GetAttributeValue("href", "");
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var timeNode = anchor.SelectSingleNode(".//div[contains(@class,'Aj')]//span")
                      ?? anchor.SelectSingleNode(".//span");
        var timeText = timeNode?.InnerText.Trim() ?? string.Empty;

        var teamRows = anchor.SelectNodes(".//div[@class='row my-auto']");
        if (teamRows is null || teamRows.Count < 2)
        {
            return null;
        }

        var home = ParseTeam(teamRows[0], teams);
        var away = ParseTeam(teamRows[1], teams);

        ParseTime(timeText, out var startUtc, out var status);

        return new Models.Match
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Slug = SlugFromUrl(href),
            LeagueSlug = leagueSlug,
            SportSlug = sportSlug,
            HomeTeam = home.Name,
            AwayTeam = away.Name,
            HomeTeamLogo = home.Logo,
            AwayTeamLogo = away.Logo,
            StartTimeUtc = startUtc,
            Status = status,
            SourceUrl = href,
            Streams = new List<StreamSource>
            {
                new() { Label = "Watch on source", Url = href }
            }
        };
    }

    private (string Name, string Logo) ParseTeam(HtmlNode row, Dictionary<string, Team> teams)
    {
        var img = row.SelectSingleNode(".//img");
        var name = img?.GetAttributeValue("alt", "")?.Trim()
                   ?? row.InnerText.Trim();
        var logo = img is null ? string.Empty : MakeAbsolute(img.GetAttributeValue("src", ""));

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "TBD";
        }

        if (!teams.ContainsKey(name))
        {
            teams[name] = new Team
            {
                Slug = Slug.Slugify(name),
                Name = name,
                Logo = logo
            };
        }

        return (name, logo);
    }

    private static void ParseTime(string text, out DateTime startUtc, out string status)
    {
        var lower = text.ToLowerInvariant();
        startUtc = DateTime.UtcNow;

        if (lower.Contains("ended"))
        {
            status = "replay";
            startUtc = DateTime.UtcNow.AddHours(-2);
            return;
        }

        if (lower.Contains("started") || lower.Contains("live"))
        {
            status = "live";
            startUtc = DateTime.UtcNow;
            return;
        }

        var hours = 0;
        var minutes = 0;
        var hMatch = Regex.Match(lower, @"(\d+)\s*hr");
        if (hMatch.Success) hours = int.Parse(hMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var mMatch = Regex.Match(lower, @"(\d+)\s*min");
        if (mMatch.Success) minutes = int.Parse(mMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        if (hours == 0 && minutes == 0)
        {
            status = "upcoming";
            startUtc = DateTime.UtcNow.AddHours(1);
            return;
        }

        status = "upcoming";
        startUtc = DateTime.UtcNow.AddHours(hours).AddMinutes(minutes);
    }

    private static (string Slug, string Name) NormalizeSport(string category)
    {
        var lowered = category.ToLowerInvariant();
        foreach (var kvp in SportMap)
        {
            if (lowered.Contains(kvp.Key))
            {
                return kvp.Value;
            }
        }

        return (Slug.Slugify(category), CultureInfo.InvariantCulture.TextInfo.ToTitleCase(category.ToLowerInvariant()));
    }

    private static string SlugFromUrl(string url)
    {
        Uri uri;
        try
        {
            uri = new Uri(url);
        }
        catch
        {
            return Slug.Slugify(url);
        }

        var segments = uri.Segments
            .Select(s => s.Trim('/'))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        foreach (var segment in ((IEnumerable<string>)segments).Reverse())
        {
            if (segment.Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Regex.IsMatch(segment, @"^[a-z0-9-]+$") && !Regex.IsMatch(segment, @"^\d+$"))
            {
                return segment;
            }
        }

        return segments.Length > 0 ? segments[^1] : Slug.Slugify(url);
    }

    private static string NormalizeBaseUrl(string? sourceUrl)
    {
        var url = (sourceUrl ?? "https://total-sportek.st/").Trim();
        if (!url.EndsWith("/"))
        {
            url += "/";
        }

        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        return url;
    }

    private string MakeAbsolute(string src)
    {
        if (string.IsNullOrWhiteSpace(src)) return string.Empty;
        if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return src;
        if (src.StartsWith("//")) return "https:" + src;
        if (src.StartsWith("/")) return _baseUrl.TrimEnd('/') + src;
        return src;
    }
}
