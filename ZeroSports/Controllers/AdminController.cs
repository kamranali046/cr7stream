using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public AdminController(IAdminLogic admin, IScrapperLogic scrapper, IConfiguration configuration)
        {
            _admin = admin;
            _scrapper = scrapper;
            _configuration = configuration;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var data = await _scrapper.GetFixturesAsync();
            var categories = data.Leagues
                .OrderBy(l => l.Name)
                .Select(league => new AdminCategoryViewModel
                {
                    League = league,
                    Matches = data.Matches
                        .Where(m => m.LeagueSlug == league.Slug)
                        .OrderBy(m => m.StartTimeUtc)
                        .ToList()
                })
                .ToList();

            return View(categories);
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
            var username = _configuration["Admin:Username"] ?? "admin";
            var password = _configuration["Admin:Password"] ?? "admin123";

            if (ModelState.IsValid &&
                model.Username == username &&
                model.Password == password)
            {
                var claims = new[] { new Claim(ClaimTypes.Name, model.Username) };
                var identity = new ClaimsIdentity(claims, "Admin");
                await HttpContext.SignInAsync("Admin", new ClaimsPrincipal(identity));
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
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
            await _scrapper.ScrapeAndSaveAsync(cancellationToken);
            return RedirectToAction(nameof(Index));
        }
    }
}
