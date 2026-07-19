using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using PerformanceLab.Application.Users;

namespace PerformanceLab.Api.Controllers;

[ApiController]
[Route("users")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [OutputCache(Duration = 60)]
    public IActionResult GetUsers()
    {
        return Ok(_userService.GetUsers());
    }
}