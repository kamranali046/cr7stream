using Microsoft.AspNetCore.Mvc;
using ZeroSports.Logic;

namespace ZeroSports.Controllers
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
            if (model is null)
            {
                return NotFound();
            }

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
    }
}
