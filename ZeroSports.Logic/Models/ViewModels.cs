namespace ZeroSports.Logic.Models;

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
    public List<LeagueSection> Sections { get; set; } = new();
}

public class SportPageViewModel
{
    public Sport? Sport { get; set; }
    public List<Match> LiveMatches { get; set; } = new();
    public List<Match> UpcomingMatches { get; set; } = new();
}

public class MatchDetailViewModel
{
    public Match? Match { get; set; }
    public League? League { get; set; }
    public Sport? Sport { get; set; }
}
