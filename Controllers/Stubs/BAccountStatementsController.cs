using Microsoft.AspNetCore.Mvc;

namespace PickNBook.Api.Controllers
{
    public class BAccountStatementsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
