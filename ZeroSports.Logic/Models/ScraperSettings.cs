namespace ZeroSports.Logic.Models;

public class ScraperSettings
{
    public string SourceUrl { get; set; } = "https://total-sportek.st/";
    public int IntervalMinutes { get; set; } = 60;

    // How many minutes before a match starts we begin pulling its player list
    // from the match link (total-sportek publishes players ~1h before start).
    public int PlayerFetchLeadMinutes { get; set; } = 40;
}
