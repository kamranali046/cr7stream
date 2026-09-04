using Microsoft.AspNetCore.Mvc;
using cr7stream.Logic;

namespace cr7stream.Components
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

