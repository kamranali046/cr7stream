using Microsoft.AspNetCore.Mvc;
using ZeroSports.Logic;

namespace ZeroSports.Components
{
    public class MainNavViewComponent : ViewComponent
    {
        private readonly IHomeControllerLogic _logic;

        public MainNavViewComponent(IHomeControllerLogic logic)
        {
            _logic = logic;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var home = await _logic.GetHomeAsync();
            return View(home.Sports);
        }
    }
}
