using Microsoft.AspNetCore.Mvc;
using cr7stream.Logic;
using cr7stream.Logic.Models;

namespace cr7stream.Controllers
{
    public class MatchesController : Controller
    {
        private readonly IMatchesControllerLogic _logic;

        public MatchesController(IMatchesControllerLogic logic)
        {
            _logic = logic;
        }

        [Route("{sport}")]
        public async Task<IActionResult> Sport(string sport)
        {
            var model = await _logic.GetBySportAsync(sport);
            return View(model);
        }

        [Route("hd/{slug}")]
        public async Task<IActionResult> Details(string slug)
        {
            var model = await _logic.GetMatchAsync(slug);
            if (model?.Match is null)
            {
                return NotFound();
            }

            return View(model);
        }

        [Route("hd/{slug}/players")]
        public async Task<IActionResult> Players(string slug)
        {
            var players = await _logic.GetMatchPlayersAsync(slug);
            return Json(players ?? new List<Player>());
        }

        [Route("team/{slug}")]
        public async Task<IActionResult> Team(string slug)
        {
            var model = await _logic.GetByTeamAsync(slug);
            return View(model);
        }
    }
}

