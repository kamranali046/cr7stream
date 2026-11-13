using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using cr7stream.Logic;
using cr7stream.Logic.Services;

namespace cr7stream.Services;

/// <summary>
/// Periodically re-scrapes the configured source and saves fixtures.json.
/// The interval (and source) are editable from the admin panel; changes apply
/// on the next cycle.
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
        _logger.LogInformation("AutoScraperService background worker starting.");

        // Two independent background loops: a daily full scrape and a frequent
        // live-window monitor that keeps live/ended flags and player lists fresh.
        var dailyTask = RunDailyScrapeLoopAsync(stoppingToken);
        var liveTask = RunLiveWindowLoopAsync(stoppingToken);

        await Task.WhenAll(dailyTask, liveTask);
    }

    private async Task RunLiveWindowLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var scrapper = scope.ServiceProvider.GetRequiredService<IScrapperLogic>();
                    await scrapper.ProcessLiveWindowsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live-window processing failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RunDailyScrapeLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan dailyTime = new TimeSpan(9, 0, 0);
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var settings = scope.ServiceProvider.GetRequiredService<IScraperSettingsProvider>();
                    var cfg = await settings.LoadAsync(stoppingToken);
                    if (cfg != null && TimeSpan.TryParse(cfg.DailyScrapeTime, out var parsed))
                    {
                        dailyTime = parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read scraper settings. Using default 09:00 PKT.");
            }

            // Calculate delay until the next daily run time (Pakistan Time, UTC+5).
            var pktOffset = TimeSpan.FromHours(5);
            var nowUtc = DateTime.UtcNow;
            var nowPkt = nowUtc + pktOffset;

            var nextPkt = new DateTime(nowPkt.Year, nowPkt.Month, nowPkt.Day,
                dailyTime.Hours, dailyTime.Minutes, 0, DateTimeKind.Unspecified);

            if (nextPkt <= nowPkt)
            {
                nextPkt = nextPkt.AddDays(1);
            }

            var nextUtc = nextPkt - pktOffset;
            var delay = nextUtc - nowUtc;

            _logger.LogInformation("Next auto-scrape scheduled for {Time} PKT (in {Delay}).", nextPkt, delay);

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var scrapper = scope.ServiceProvider.GetRequiredService<IScrapperLogic>();
                    await scrapper.ScrapeFixturesAndDrillPlayersAsync(stoppingToken);
                    _logger.LogInformation("Auto-scrape successfully completed (fixtures + players).");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-scrape cycle failed.");
            }

            // Small safety buffer to avoid tight loops if time calculations overlap
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

