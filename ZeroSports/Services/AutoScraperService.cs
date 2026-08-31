using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZeroSports.Logic;
using ZeroSports.Logic.Services;

namespace ZeroSports.Services;

/// <summary>
/// Periodically re-scrapes the configured source and saves fixtures.json.
/// The interval (and source) are editable from the admin panel; changes apply
/// on the next cycle. Waits one interval before the first run so a restart does
/// not immediately overwrite manually curated data.
/// </summary>
public class AutoScraperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoScraperService> _logger;

    public AutoScraperService(IServiceScopeFactory scopeFactory, ILogger<AutoScraperService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Two independent background loops: a daily full scrape and a frequent
        // live-window monitor that keeps live/ended flags and player lists fresh.
        var dailyTask = RunDailyScrapeLoop(stoppingToken);
        var liveTask = RunLiveWindowLoop(stoppingToken);

        await Task.WhenAll(dailyTask, liveTask);
    }

    private async Task RunLiveWindowLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scrapper = scope.ServiceProvider.GetRequiredService<IScrapperLogic>();
                await scrapper.ProcessLiveWindowsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live-window processing failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunDailyScrapeLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan dailyTime;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IScraperSettingsProvider>();
                var cfg = await settings.LoadAsync(stoppingToken);
                dailyTime = TimeSpan.TryParse(cfg.DailyScrapeTime, out var parsed)
                    ? parsed
                    : new TimeSpan(9, 0, 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read scraper settings. Using default 09:00.");
                dailyTime = new TimeSpan(9, 0, 0);
            }

            // Wait until the next daily run time (local time).
            var now = DateTime.Now;
            var next = new DateTime(now.Year, now.Month, now.Day,
                dailyTime.Hours, dailyTime.Minutes, 0);
            if (next <= now)
            {
                next = next.AddDays(1);
            }

            _logger.LogInformation("Next auto-scrape scheduled for {Time}.", next);

            try
            {
                await Task.Delay(next - now, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scrapper = scope.ServiceProvider.GetRequiredService<IScrapperLogic>();
                await scrapper.ScrapeAndSaveAsync(stoppingToken, drillPlayers: false);
                _logger.LogInformation("Auto-scrape completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-scrape failed.");
            }
        }
    }
}
