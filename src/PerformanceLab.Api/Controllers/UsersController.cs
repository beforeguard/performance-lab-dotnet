using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using PerformanceLab.Shared.Configuration;
using PerformanceLab.Shared.DTOs;
using PerformanceLab.Application.Users;
using PerformanceLab.Application.Users.Models;

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
    public IActionResult GetUsers([FromQuery] int? offset = null, [FromQuery] int? limit = null)
    {
        // Validation: if limit specified, validate offset and limit
        if (limit.HasValue)
        {
            if (offset < 0)
            {
                return BadRequest(new { error = "Offset must be greater than or equal to 0." });
            }

            if (limit <= 0)
            {
                return BadRequest(new { error = "Limit must be greater than 0." });
            }
        }

        // Add headers to indicate which features are active
        Response.Headers["X-Caching-Enabled"] = _perfFeatures.EnableOutputCaching.ToString();
        Response.Headers["X-Pooling-Enabled"] = _perfFeatures.EnableObjectPooling.ToString();
        Response.Headers["X-Streaming-Enabled"] = _perfFeatures.EnableStreaming.ToString();
        
        var users = _userService.GetUsers(offset, limit);
        
        // Dispose after response completes if needed (e.g., PooledUserDtoCollection when streaming)
        if (users is IDisposable disposable)
        {
            Response.OnCompleted(() =>
            {
                disposable.Dispose();
                return Task.CompletedTask;
            });
        }

        // Return paginated result or raw array based on whether pagination was requested
        if (limit.HasValue)
        {
            var pagedResult = new PagedResult<UserDto>
            {
                Items = users.ToList(),
                Total = _userService.GetCount(),
                Offset = offset ?? 0,
                Limit = limit.Value
            };
            return Ok(pagedResult);
        }
        
        return Ok(users);
    }
}