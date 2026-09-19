namespace ProtoTest.AspNetCore.SampleApi;

using Microsoft.AspNetCore.Mvc;

/// <summary>
/// A non-API MVC controller: only the action with explicit HTML evidence is a page for the inventory,
/// the JSON action is not.
/// </summary>
public sealed class PagesController : Controller
{
    [HttpGet("/mvc/json")]
    public IActionResult JsonAction() => Json(new { ok = true });

    [HttpGet("/mvc/html")]
    [Produces("text/html")]
    public IActionResult HtmlAction() => Content("<h1>Page</h1>", "text/html");

    [HttpGet("/mvc/view")]
    public ViewResult ViewAction() => View();
}

/// <summary>
/// An API controller whose JSON action is not a page and whose HTML-producing action is one, even while
/// it lives under an API-shaped route.
/// </summary>
[ApiController]
[Route("api/legacy")]
public sealed class LegacyApiController : ControllerBase
{
    [HttpGet("orders")]
    public IActionResult Orders() => Ok(new[] { "order-1" });

    [HttpGet("report")]
    [Produces("text/html")]
    public IActionResult Report() => Content("<h1>Report</h1>", "text/html");

    [HttpGet("view")]
    public ViewResult ViewAction() => new();
}
