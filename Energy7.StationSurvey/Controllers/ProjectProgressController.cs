using Energy7.StationSurvey.Utility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace Energy7.StationSurvey.Controllers
{
    [Authenticate]
    public class ProjectProgressController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public ProjectProgressController(IWebHostEnvironment env)
        {
            _env = env;
        }

        // GET: /ProjectProgress
        public IActionResult Index()
        {
            return View();
        }

        // GET: /ProjectProgress/Page  (the page shown inside the menu frame)
        public IActionResult Page()
        {
            var path = Path.Combine(_env.ContentRootPath, "App_Data", "rdpms_project_progress.html");
            return PhysicalFile(path, "text/html; charset=utf-8");
        }
    }
}
