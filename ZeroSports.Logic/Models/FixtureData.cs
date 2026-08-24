namespace ZeroSports.Logic.Models;

public class Sport
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;
}

public class League
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SportSlug { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;
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
    public List<StreamSource> Streams { get; set; } = new();
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
}
