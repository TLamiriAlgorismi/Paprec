using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using MvcClient.Models;
using Newtonsoft.Json.Linq;

namespace MvcClient.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        var result = HttpContext.AuthenticateAsync("oidc").Result;
        if (result?.Succeeded != true)
        {
            throw new Exception("External authentication error");
        }

        // retrieve claims of the external user
        var externalUser = result.Principal;
        if (externalUser == null)
        {
            throw new Exception("External authentication error");
        }

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public async Task<IActionResult> CallApi()
    {
        var accessToken = await HttpContext.GetTokenAsync("access_token");

        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var content = await client.GetStringAsync("https://localhost:6001/identity");

        ViewBag.Json = JArray.Parse(content).ToString();
        return View("json");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
