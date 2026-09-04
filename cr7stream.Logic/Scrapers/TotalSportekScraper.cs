using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using cr7stream.Logic.Models;
using cr7stream.Logic.Services;

namespace cr7stream.Logic.Scrapers;

public interface ITotalSportekScraper
{
    Task<FixtureData> ScrapeAsync(CancellationToken cancellationToken = default, bool drillPlayers = false);
    Task<FixtureData> ScrapeFixturesAsync(CancellationToken cancellationToken = default);
    Task DrillPlayersAsync(FixtureData data, CancellationToken cancellationToken = default, int maxConcurrent = 6);
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

    public async Task<FixtureData> ScrapeAsync(CancellationToken cancellationToken = default, bool drillPlayers = false)
    {
        var data = await ScrapeInternalAsync(cancellationToken);

        if (drillPlayers)
        {
            await DrillPlayersAsync(data, cancellationToken);
        }

        return data;
    }

    public async Task<List<Player>> GetPlayersAsync(string matchUrl, CancellationToken cancellationToken = default)
    {
        return await ExtractPlayersHttpAsync(matchUrl, cancellationToken);
    }

    public async Task<FixtureData> ScrapeFixturesAsync(CancellationToken cancellationToken = default)
    {
        return await ScrapeInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Phase 2: Drill players for eligible matches (live or within PlayerFetchLeadMinutes).
    /// Modifies data.Matches in-place. Only visits source URLs for qualifying matches.
    /// </summary>
    public async Task DrillPlayersAsync(FixtureData data, CancellationToken cancellationToken = default, int maxConcurrent = 6)
    {
        var settings = _settings is null
            ? new ScraperSettings()
            : await _settings.LoadAsync(cancellationToken);

        var lead = TimeSpan.FromMinutes(settings.PlayerFetchLeadMinutes);
        var now = DateTime.UtcNow;

        var eligible = data.Matches.Where(m =>
        {
            if (string.IsNullOrWhiteSpace(m.SourceUrl)) return false;
            if (m.IsLive || m.Status == "live") return true;
            var before = m.StartTimeUtc - now;
            return before <= lead && before > TimeSpan.FromHours(-3);
        })
            .OrderByDescending(m => m.IsLive || m.Status == "live")
            .Take(30)
            .ToList();

        if (eligible.Count == 0) return;

        using var sem = new SemaphoreSlim(maxConcurrent);
        var tasks = eligible.Select(async m =>
        {
            await sem.WaitAsync(cancellationToken);
            try
            {
                m.Players = await ExtractPlayersHttpAsync(m.SourceUrl!, cancellationToken, maxPlayers: 10);
            }
            finally
            {
                sem.Release();
            }
        });
        await Task.WhenAll(tasks);
    }

    private async Task<FixtureData> ScrapeInternalAsync(CancellationToken ct)
    {
        var settings = _settings is null
            ? new ScraperSettings()
            : await _settings.LoadAsync(ct);
        _baseUrl = NormalizeBaseUrl(settings.SourceUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl);
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
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
                if (match is not null && match.Status != "replay")
                {
                    matches.Add(match);
                }
            }
        }

        data.Leagues = leagues.Values.ToList();
        data.Sports = sports.Values.ToList();
        data.Teams = teams.Values.ToList();
        data.Matches = matches;

        foreach (var mt in data.Matches)
        {
            mt.IsLive = mt.Status == "live";
            mt.IsEnded = mt.Status == "replay";
        }

        return data;
    }

    private static bool IsJunkHost(Uri uri)
    {
        var h = uri.Host.ToLowerInvariant();
        return h.Contains("chatango.")
               || h.Contains("doubleclick.")
               || h.Contains("googlesyndication.")
               || h.Contains("google.")
               || h.Contains("facebook.")
               || h.Contains("twitter.")
               || h.Contains("instagram.")
               || h.Contains("t.co")
               || h.Contains("crwdcntrl.")
               || h.Contains("criteo.")
               || h.Contains("adsystem.")
               || h.Contains("adnxs.")
               || h.Contains("pubmatic.")
               || h.Contains("amazon-adsystem.");
    }

    private async Task<List<Player>> ExtractPlayersHttpAsync(string url, CancellationToken cancellationToken, int maxPlayers = 10)
    {
        var players = new List<Player>();
        if (string.IsNullOrWhiteSpace(url))
        {
            return players;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseHost = SafeHost(_baseUrl);

        var html = await FetchHtmlAsync(MakeAbsolute(url), cancellationToken);
        if (html is null)
        {
            return players;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // The player list is rendered in dedicated rows (.data-row / .btn-watch, and
        // the #streams .table-row table). Each row links to a provider/wrapper page
        // that hosts the real player iframe. Per the reference Node scraper we don't
        // need a browser: we just fetch each wrapper once and grab its <iframe src>.
        var linkXpaths = new[]
        {
            "//div[@id='streams']//a[@href]",
            "//*[contains(concat(' ',normalize-space(@class),' '),' data-row ')]//a[@href]",
            "//*[contains(concat(' ',normalize-space(@class),' '),' btn-watch ')]//a[@href]",
        };

        var candidates = new List<(string Href, string Name)>();
        foreach (var xp in linkXpaths)
        {
            foreach (var a in doc.DocumentNode.SelectNodes(xp) ?? Enumerable.Empty<HtmlNode>())
            {
                var href = MakeAbsolute(a.GetAttributeValue("href", ""));
                if (!IsValidPlayerCandidate(href, baseHost))
                {
                    continue;
                }

                // Extract the streamer name from the first column only.
                // Row structure: <div class="data-row"><a><div class="row">
                //   <div class="col-md-3 col-5">StreamerName <span>...</span></div>
                //   <div class="col-md-3 col-5">Channel</div> ...
                // </div></a></div>
                var name = "";
                var firstCol = a.SelectSingleNode(".//div[contains(@class,'col-md-3') and contains(@class,'col-5')]");
                if (firstCol != null)
                {
                    // Get direct text only (skip nested spans like golden-tick)
                    name = System.Net.WebUtility.HtmlDecode(
                        string.Concat(firstCol.ChildNodes
                            .Where(n => n.Name == "#text" || (n.Name != "span" && n.Name != "i"))
                            .Select(n => n.InnerText))
                    ).Trim();
                    name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    name = System.Net.WebUtility.HtmlDecode((a.InnerText ?? "").Trim());
                    name = System.Text.RegularExpressions.Regex.Replace(name, @"&(?:#x?[0-9A-Fa-f]+|[a-z]+);?", " ").Trim();
                    // Take only the first word-like chunk before junk text
                    var m = System.Text.RegularExpressions.Regex.Match(name, @"^([A-Za-z0-9\s._-]+)");
                    if (m.Success) name = m.Groups[1].Value.Trim();
                }
                if (name.Length > 40) name = name[..40];

                candidates.Add((href, name));
            }
        }

        // De-duplicate by href, preserving order.
        var dedup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (href, name) in candidates)
        {
            if (!dedup.ContainsKey(href)) dedup[href] = name;
        }

        // Drill each wrapper in parallel (bounded) so the whole fetch finishes in
        // roughly the time of the single slowest provider rather than the sum.
        // Use a pre-sized array to preserve source order (top = fewer ads).
        var ordered = dedup.ToList();
        var results = new (string Url, string Name)?[ordered.Count];
        using var sem = new SemaphoreSlim(5);
        var tasks = new List<Task>();
        for (var i = 0; i < Math.Min(ordered.Count, maxPlayers); i++)
        {
            var idx = i;
            var h = ordered[i].Key;
            var n = ordered[i].Value;
            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync(cancellationToken);
                try
                {
                    var resolved = await ResolvePlayerIframeAsync(h, cancellationToken);
                    var finalUrl = resolved ?? h;
                    results[idx] = (finalUrl, n);
                }
                finally
                {
                    sem.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);

        // Build the final list in source order.
        // Top 6 enabled by default (fewer ads), rest disabled but available.
        var pos = 0;
        foreach (var r in results)
        {
            if (r is { } entry)
            {
                if (AddPlayer(players, seen, entry.Url, baseHost,
                    string.IsNullOrWhiteSpace(entry.Name) ? null : entry.Name, strict: false))
                {
                    players[^1].Enabled = pos < 6;
                    pos++;
                }
            }
        }

        return players;
    }

    /// <summary>
    /// Fetches a provider/wrapper page once and returns the src of its first
    /// non-junk &lt;iframe&gt;. The wrapper page (e.g. .../stream-1.php) is itself
    /// the embeddable URL — it internally frames the real player with the correct
    /// referer, whereas the inner player host rejects direct/third-party requests
    /// (403). We therefore return the wrapper URL, and the front-end iframe uses
    /// referrerpolicy="no-referrer" so the wrapper loads (the host allows a missing
    /// referer) and then chains to the actual stream.
    /// </summary>
    private async Task<string?> ResolvePlayerIframeAsync(string wrapperUrl, CancellationToken ct, int depth = 0)
    {
        if (depth > 3) return null;
        try
        {
            var innerHtml = await FetchHtmlAsync(wrapperUrl, ct, TimeSpan.FromSeconds(12));
            if (innerHtml is null) return null;

            var inner = new HtmlDocument();
            inner.LoadHtml(innerHtml);

            // If the page itself has a <video> or <source> tag, it's a self-contained
            // player page (e.g. rippleplays.cfd/stream-40.php). Return the wrapper URL.
            var hasVideo = inner.DocumentNode.SelectSingleNode("//video") != null
                           || inner.DocumentNode.SelectSingleNode("//source") != null;

            var iframes = inner.DocumentNode.SelectNodes("//iframe")?.ToList()
                          ?? new List<HtmlNode>();

            var bestCandidate = (string?)null;
            var wrapperHost = Uri.TryCreate(wrapperUrl, UriKind.Absolute, out var wu) ? wu.Host : "";

            foreach (var f in iframes)
            {
                var src = f.GetAttributeValue("src", "").Trim();
                if (string.IsNullOrWhiteSpace(src)) continue;

                var hasAllowFullscreen = f.Attributes["allowfullscreen"] != null;
                var hasWidth = !string.IsNullOrEmpty(f.GetAttributeValue("width", ""));
                var hasHeight = !string.IsNullOrEmpty(f.GetAttributeValue("height", ""));

                var abs = MakeAbsoluteFrom(src, wrapperUrl);
                if (!Uri.TryCreate(abs, UriKind.Absolute, out var u)) continue;
                if (u.Host.IndexOf('.') <= 0) continue;
                if (IsJunkHost(u)) continue;

                var pathLower = u.PathAndQuery.ToLowerInvariant();
                var isSameHost = string.Equals(u.Host, wrapperHost, StringComparison.OrdinalIgnoreCase);

                // Skip .html/.htm URLs on same host — they're navigation wrappers
                if ((pathLower.EndsWith(".html") || pathLower.EndsWith(".htm")) && isSameHost)
                {
                    var nested = await ResolvePlayerIframeAsync(abs, ct, depth + 1);
                    if (nested != null) return nested;
                    continue;
                }

                // If this is a different host with player attributes, it's likely
                // the actual player embed. But if the current page already has a
                // <video> tag, the current page IS the player — return it.
                if (hasVideo && isSameHost)
                {
                    return wrapperUrl;
                }

                if (hasAllowFullscreen && hasWidth && hasHeight)
                {
                    return abs;
                }

                bestCandidate ??= hasAllowFullscreen ? abs : null;
            }

            // No iframe worked, but the page itself has video content
            if (hasVideo && bestCandidate == null && iframes.Count == 0)
            {
                return wrapperUrl;
            }

            return bestCandidate;
        }
        catch
        {
            // best-effort
        }

        return null;
    }

    private async Task<string?> FetchHtmlAsync(string url, CancellationToken ct, TimeSpan? timeout = null)
    {
        try
        {
            var effective = timeout ?? TimeSpan.FromSeconds(15);
            using var timeoutCts = new CancellationTokenSource(effective);
            using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var token = innerCts.Token;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            using var response = await _http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(token);
        }
        catch
        {
            // best-effort: a single failed page must not break the scrape
            return null;
        }
    }

    private static bool IsValidPlayerCandidate(string raw, string? baseHost)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (baseHost is not null && string.Equals(uri.Host, baseHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Provider/wrapper links often don't contain "stream" in their own path, so we
        // only reject obvious tracking/ad hosts here. The resolved iframe is validated later.
        return !IsJunkHost(uri);
    }

    private bool AddPlayer(List<Player> players, HashSet<string> seen, string raw, string? baseHost)
        => AddPlayer(players, seen, raw, baseHost, null, true);

    private bool AddPlayer(List<Player> players, HashSet<string> seen, string raw, string? baseHost, string? name, bool strict = true)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var url = MakeAbsolute(raw);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (baseHost is not null && string.Equals(uri.Host, baseHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsJunkHost(uri))
        {
            return false;
        }

        if (strict && !LooksLikePlayer(uri))
        {
            return false;
        }

        if (!seen.Add(uri.AbsoluteUri))
        {
            return false;
        }

        players.Add(new Player
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Player {players.Count + 1}" : name!,
            Url = uri.AbsoluteUri,
            Enabled = true
        });

        return true;
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
        // Bare host, optionally with path/query (e.g. "smartcric.top//watch.html?id=1").
        var slash = src.IndexOf('/');
        var host = slash >= 0 ? src.Substring(0, slash) : src;
        if (host.IndexOf('.') > 0 && !host.Contains(' ') && Uri.CheckHostName(host) != UriHostNameType.Unknown)
        {
            return "https://" + src;
        }
        return src;
    }

    private static string MakeAbsoluteFrom(string src, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(src)) return string.Empty;
        if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return src;
        if (src.StartsWith("//")) return "https:" + src;
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var bu))
        {
            if (Uri.TryCreate(bu, src, out var resolved))
            {
                return resolved.ToString();
            }
        }
        // Fallback: prepend base origin
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            var origin = baseUri.GetLeftPart(UriPartial.Authority);
            if (src.StartsWith("/"))
                return origin + src;
            var dir = origin + baseUri.AbsolutePath;
            dir = dir.Substring(0, dir.LastIndexOf('/') + 1);
            return dir + src;
        }
        return src;
    }
}

