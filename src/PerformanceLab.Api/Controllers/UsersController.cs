using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using PerformanceLab.Shared.Configuration;
using PerformanceLab.Application.Users;

namespace PerformanceLab.Api.Controllers;

[ApiController]
[Route("users")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;
    private readonly PerformanceFeatures _perfFeatures;

    public UsersController(
        UserService userService, 
        IConfiguration configuration)
    {
        _userService = userService;
        _perfFeatures = configuration
            .GetSection("PerformanceFeatures")
            .Get<PerformanceFeatures>() ?? new PerformanceFeatures();
    }

    [HttpGet]
    [OutputCache(PolicyName = "UsersCachePolicy")] // Only active when enabled
    public IActionResult GetUsers()
    {
        // Add headers to indicate which features are active
        Response.Headers["X-Caching-Enabled"] = _perfFeatures.EnableOutputCaching.ToString();
        Response.Headers["X-Pooling-Enabled"] = _perfFeatures.EnableObjectPooling.ToString();
        Response.Headers["X-Streaming-Enabled"] = _perfFeatures.EnableStreaming.ToString();
        
        var users = _userService.GetUsers();
        
        // Dispose after response completes if needed (e.g., PooledUserDtoCollection when streaming)
        if (users is IDisposable disposable)
        {
            Response.OnCompleted(() =>
            {
                disposable.Dispose();
                return Task.CompletedTask;
            });
        }
        
        return Ok(users);
    }
}