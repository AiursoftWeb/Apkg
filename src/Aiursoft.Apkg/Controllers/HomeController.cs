using Aiursoft.Apkg.Models.HomeViewModels;
using Aiursoft.Apkg.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.Apkg.Controllers;

[LimitPerMin]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return this.SimpleView(new IndexViewModel());
    }
}
