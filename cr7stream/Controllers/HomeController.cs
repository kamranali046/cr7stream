using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using cr7stream.Logic;
using cr7stream.Models;

namespace cr7stream.Controllers
{
    public class HomeController : Controller
    {
        private readonly IHomeControllerLogic _logic;

        public HomeController(IHomeControllerLogic logic)
        {
            _logic = logic;
        }

        public async Task<IActionResult> Index()
        {
            var model = await _logic.GetHomeAsync();
            return View(model);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

