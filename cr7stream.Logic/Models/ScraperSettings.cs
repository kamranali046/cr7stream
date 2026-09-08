namespace cr7stream.Logic.Models;

public class ScraperSettings
{
    public string SourceUrl { get; set; } = "https://total-sportek.st/";
    public string? DailyScrapeTime { get; set; } = "09:00";
    public int PlayerFetchLeadMinutes { get; set; } = 40;
    public int LiveMarkLeadMinutes { get; set; } = 10;
    public int LiveAutoEndHours { get; set; } = 4;
    public bool ShowBanner { get; set; } = true;
}

