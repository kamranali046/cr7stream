namespace cr7stream.Logic.Models;

public class ScraperSettings
{
    public string SourceUrl { get; set; } = "https://total-sportek.st/";

    // Daily schedule (local time, "HH:mm" 24h). The background scheduler runs the
    // scrape exactly once every day at this time to fetch that day's fixtures.
    // Defaults to 09:00 (9 AM) if not set.
    public string? DailyScrapeTime { get; set; } = "09:00";

    // How many minutes before a match starts we begin pulling its player list
    // from the match link (total-sportek publishes players ~1h before start).
    public int PlayerFetchLeadMinutes { get; set; } = 40;

    // How many minutes before kickoff the scheduler proactively flags the match
    // as LIVE (so the player screen starts loading even before the source does).
    public int LiveMarkLeadMinutes { get; set; } = 10;

    // A match that has been marked live for longer than this is auto-flagged as
    // ended, so a stuck "LIVE NOW" badge can't linger for days.
    public int LiveAutoEndHours { get; set; } = 4;
}

