namespace cr7stream.Logic.Models;

public class Sport
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;

    // Admin-added sports are preserved across auto-scrapes.
    public bool IsCustom { get; set; }
}

public class League
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SportSlug { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;

    // Admin-added leagues are preserved across auto-scrapes.
    public bool IsCustom { get; set; }

    // Hidden (inactive) categories are removed from the public site.
    public bool Hidden { get; set; }
}

public class Team
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;
}

public class Match
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string LeagueSlug { get; set; } = string.Empty;
    public string SportSlug { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string HomeTeamLogo { get; set; } = string.Empty;
    public string AwayTeamLogo { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; }
    public string Status { get; set; } = "upcoming";
    public string SourceUrl { get; set; } = string.Empty;
    public List<StreamSource> Streams { get; set; } = new();

    // Streaming "players" pulled from the match link (or added by the admin).
    public List<Player> Players { get; set; } = new();

    // Admin flags (preserved across auto-scrapes via slug matching).
    public bool IsCustom { get; set; }
    public bool Important { get; set; }
    public bool IsLive { get; set; }
    public bool IsEnded { get; set; }
    // When true, the admin manually overrode live/ended and the scraper must
    // NOT re-sync these flags from the source status.
    public bool LiveStateLocked { get; set; }
}

public class Player
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    // Admin-added players survive auto-scrapes; scraped ones are refreshed.
    public bool IsCustom { get; set; }
}

public class StreamSource
{
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class FixtureData
{
    public List<Sport> Sports { get; set; } = new();
    public List<League> Leagues { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
    public List<Match> Matches { get; set; } = new();

    // When true (dummy data) the logic rebases timestamps relative to "now".
    // Scraped data has real absolute timestamps and sets this to false.
    public bool NormalizeTimes { get; set; }

    public DateTime? ScrapedAtUtc { get; set; }
}

