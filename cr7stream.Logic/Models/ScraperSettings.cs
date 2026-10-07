namespace cr7stream.Logic.Models;

public class ScraperSettings
{
    public string SourceUrl { get; set; } = "https://totalsportek1.is/";
    public string? DailyScrapeTime { get; set; } = "09:00";
    public int PlayerFetchLeadMinutes { get; set; } = 40;
    public int LiveMarkLeadMinutes { get; set; } = 10;
    public int LiveAutoEndHours { get; set; } = 8;
    public bool ShowBanner { get; set; } = false;
}

