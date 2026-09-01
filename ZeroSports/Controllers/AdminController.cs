using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ZeroSports.Logic;
using ZeroSports.Logic.Models;
using ZeroSports.Models;

namespace ZeroSports.Controllers
{
    [Route("admin")]
    [Authorize(AuthenticationSchemes = "Admin")]
    public class AdminController : Controller
    {
        private readonly IAdminLogic _admin;
        private readonly IScrapperLogic _scrapper;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AdminController> _logger;
        private static readonly ConcurrentDictionary<string, List<DateTime>> _failedLogins = new(StringComparer.OrdinalIgnoreCase);

        public AdminController(IAdminLogic admin, IScrapperLogic scrapper, IConfiguration configuration, ILogger<AdminController> logger)
        {
            _admin = admin;
            _scrapper = scrapper;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var data = await _scrapper.GetFixturesAsync();
            var settings = await _admin.GetSettingsAsync(cancellationToken);

            ViewData["SourceUrl"] = settings.SourceUrl;
            ViewData["DailyScrapeTime"] = settings.DailyScrapeTime ?? "09:00";
            ViewData["LastScraped"] = data.ScrapedAtUtc;

            var categories = data.Leagues
                .Select(league => new AdminCategoryViewModel
                {
                    League = league,
                Matches = data.Matches
                    .Where(m => m.LeagueSlug == league.Slug)
                    .ToList()
                })
                .ToList();

            return View(categories);
        }

        [HttpGet("settings")]
        public async Task<IActionResult> Settings(CancellationToken cancellationToken)
        {
            var settings = await _admin.GetSettingsAsync(cancellationToken);
            return View(settings);
        }

        [HttpPost("settings")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(ScraperSettings model, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(model.DailyScrapeTime)
                && !TimeSpan.TryParse(model.DailyScrapeTime, out _))
            {
                ModelState.AddModelError(nameof(model.DailyScrapeTime), "Use HH:mm (24h) format, e.g. 09:00.");
                return View(model);
            }

            await _admin.SaveSettingsAsync(model, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet("login")]
        [AllowAnonymous]
        public IActionResult Login()
        {
            if (User.Identity is { IsAuthenticated: true })
            {
                return RedirectToAction(nameof(Index));
            }

            return View();
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString()
                     ?? HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                     ?? "unknown";

            if (IsLoginRateLimited(ip))
            {
                ViewData["Error"] = "Too many login attempts. Try again later.";
                return View(model);
            }

            var username = _configuration["Admin:Username"] ?? "admin";
            var password = _configuration["Admin:Password"] ?? "admin123";

            var userOk = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(model.Username ?? ""),
                Encoding.UTF8.GetBytes(username));
            var passOk = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(model.Password ?? ""),
                Encoding.UTF8.GetBytes(password));

            if (ModelState.IsValid && userOk && passOk)
            {
                _failedLogins.TryRemove(ip, out _);
                var claims = new[] { new Claim(ClaimTypes.Name, model.Username ?? string.Empty) };
                var identity = new ClaimsIdentity(claims, "Admin");
                await HttpContext.SignInAsync("Admin", new ClaimsPrincipal(identity));
                return RedirectToAction(nameof(Index));
            }

            RecordFailedLogin(ip);
            ViewData["Error"] = "Invalid username or password.";
            return View(model);
        }

        private static bool IsLoginRateLimited(string ip)
        {
            var now = DateTime.UtcNow;
            var attempts = _failedLogins.GetOrAdd(ip, _ => new List<DateTime>());
            lock (attempts)
            {
                attempts.RemoveAll(a => a < now.AddMinutes(-15));
                return attempts.Count >= 10;
            }
        }

        private static void RecordFailedLogin(string ip)
        {
            var now = DateTime.UtcNow;
            var attempts = _failedLogins.GetOrAdd(ip, _ => new List<DateTime>());
            lock (attempts)
            {
                attempts.RemoveAll(a => a < now.AddMinutes(-15));
                attempts.Add(now);
            }
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("Admin");
            return RedirectToAction(nameof(Login));
        }

        [HttpGet("category/create")]
        public IActionResult CategoryCreate()
        {
            return View();
        }

        [HttpPost("category/create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CategoryCreate(CategoryInput model, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "Category name is required.");
                return View(model);
            }

            await _admin.AddCategoryAsync(model, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("category/delete/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CategoryDelete(string slug, CancellationToken cancellationToken)
        {
            await _admin.DeleteCategoryAsync(slug, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpGet("match/create/{categorySlug}")]
        public async Task<IActionResult> MatchCreate(string categorySlug, CancellationToken cancellationToken)
        {
            var league = await _admin.GetCategoryAsync(categorySlug, cancellationToken);
            if (league is null)
            {
                return NotFound();
            }

            var model = new MatchInput
            {
                StartTime = DateTime.UtcNow.AddHours(1)
            };

            ViewData["Category"] = league;
            return View(model);
        }

        [HttpPost("match/create/{categorySlug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MatchCreate(string categorySlug, MatchInput model, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(model.HomeTeam) || string.IsNullOrWhiteSpace(model.AwayTeam))
            {
                ModelState.AddModelError(string.Empty, "Both teams are required.");
                var league = await _admin.GetCategoryAsync(categorySlug, cancellationToken);
                ViewData["Category"] = league;
                return View(model);
            }

            await _admin.AddMatchAsync(categorySlug, model, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("match/delete/{categorySlug}/{matchSlug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MatchDelete(string categorySlug, string matchSlug, CancellationToken cancellationToken)
        {
            await _admin.DeleteMatchAsync(categorySlug, matchSlug, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("scrape")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Scrape(CancellationToken cancellationToken)
        {
            try
            {
                await _scrapper.ScrapeAndSaveAsync(CancellationToken.None, drillPlayers: true);
                TempData["ScrapeMessage"] = "Re-scrape completed.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual re-scrape failed.");
                TempData["ScrapeError"] = "Re-scrape failed: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("match/{slug}")]
        public async Task<IActionResult> MatchDetails(string slug, CancellationToken cancellationToken)
        {
            var match = await _admin.GetMatchBySlugAsync(slug, cancellationToken);
            if (match is null)
            {
                return NotFound();
            }

            return View(match);
        }

        [HttpPost("category/hide/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCategoryHidden(string slug, CancellationToken cancellationToken)
        {
            await _admin.ToggleCategoryHiddenAsync(slug, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("category/move/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveCategory(string slug, string direction, CancellationToken cancellationToken)
        {
            await _admin.MoveCategoryAsync(slug, direction, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("match/important/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleImportant(string slug, CancellationToken cancellationToken)
        {
            await _admin.ToggleImportantAsync(slug, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("match/live/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleMatchLive(string slug, CancellationToken cancellationToken)
        {
            await _admin.ToggleMatchLiveAsync(slug, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("match/ended/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleMatchEnded(string slug, CancellationToken cancellationToken)
        {
            await _admin.ToggleMatchEndedAsync(slug, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("match/time/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMatchTime(string slug, string startTime, CancellationToken cancellationToken)
        {
            if (DateTime.TryParse(startTime, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
            {
                // Input is PKT (UTC+5); convert back to UTC before storing.
                await _admin.UpdateMatchTimeAsync(slug, parsed.AddHours(-5), cancellationToken);
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("matches/adjust-time")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustAllTimes(int minutes, CancellationToken cancellationToken)
        {
            if (minutes > 0)
            {
                await _admin.AdjustAllMatchTimesAsync(minutes, cancellationToken);
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("match/move/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveMatch(string slug, string direction, CancellationToken cancellationToken)
        {
            await _admin.MoveMatchAsync(slug, direction, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("match/players/refresh/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshPlayers(string slug, CancellationToken cancellationToken)
        {
            await _admin.RefreshMatchPlayersAsync(slug, cancellationToken);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("match/player/add/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPlayer(string slug, string name, string url, CancellationToken cancellationToken)
        {
            await _admin.AddPlayerAsync(slug, name, url, cancellationToken);
            return RedirectToAction(nameof(MatchDetails), new { slug });
        }

        [HttpPost("match/player/toggle/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePlayer(string slug, int index, CancellationToken cancellationToken)
        {
            await _admin.TogglePlayerAsync(slug, index, cancellationToken);
            return RedirectToAction(nameof(MatchDetails), new { slug });
        }

        [HttpPost("match/player/move/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MovePlayer(string slug, int index, string direction, CancellationToken cancellationToken)
        {
            await _admin.MovePlayerAsync(slug, index, direction, cancellationToken);
            return RedirectToAction(nameof(MatchDetails), new { slug });
        }

        [HttpPost("match/player/save/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePlayer(string slug, int index, string url, CancellationToken cancellationToken)
        {
            await _admin.SavePlayerAsync(slug, index, url, cancellationToken);
            return RedirectToAction(nameof(MatchDetails), new { slug });
        }

        [HttpPost("match/player/delete/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePlayer(string slug, int index, CancellationToken cancellationToken)
        {
            await _admin.DeletePlayerAsync(slug, index, cancellationToken);
            return RedirectToAction(nameof(MatchDetails), new { slug });
        }
    }
}
