namespace cr7stream.Logic.Models;

/// <summary>
/// Slugs/names for special virtual categories that are not tied to a single
/// scraped league. The "Important Matches" category aggregates every enabled
/// match the admin starred (or that the source lists under its own
/// "Important Games"/featured area).
/// </summary>
public static class Categories
{
    public const string ImportantMatchesSlug = "important-matches";
    public const string ImportantMatchesName = "Important Matches";
}

public class LeagueSection
{
    public League League { get; set; } = new();
    public List<Match> Matches { get; set; } = new();
}

public class HomeViewModel
{
    public List<Sport> Sports { get; set; } = new();
    public List<League> TopLeagues { get; set; } = new();
    public List<Team> TopTeams { get; set; } = new();
    public List<Match> ImportantMatches { get; set; } = new();
    public List<LeagueSection> Sections { get; set; } = new();
}

public class SportPageViewModel
{
    public Sport? Sport { get; set; }
    public List<Match> ImportantMatches { get; set; } = new();
    public List<Match> Matches { get; set; } = new();
}

public class TeamPageViewModel
{
    public Team? Team { get; set; }
    public List<Match> Matches { get; set; } = new();
}

public class MatchDetailViewModel
{
    public Match? Match { get; set; }
    public League? League { get; set; }
    public Sport? Sport { get; set; }

    // False when the match kickoff is further away than the configured lead window,
    // i.e. the source hasn't published players yet. The player screen then shows a
    // "stream will be available N minutes before…" notice instead of fetching players.
    public bool PlayersAvailable { get; set; }

    public int PlayerLeadMinutes { get; set; }
}

