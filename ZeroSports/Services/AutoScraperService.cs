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
        while (!stoppingToken.IsCancellationRequested)
        {
            int intervalMinutes;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IScraperSettingsProvider>();
                var cfg = await settings.LoadAsync(stoppingToken);
                intervalMinutes = Math.Max(1, cfg.IntervalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read scraper settings.");
                intervalMinutes = 60;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
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
                await scrapper.ScrapeAndSaveAsync(stoppingToken);
                _logger.LogInformation("Auto-scrape completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-scrape failed.");
            }
        }
    }
}
