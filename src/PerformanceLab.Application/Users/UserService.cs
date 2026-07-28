using System.Buffers;
using PerformanceLab.Shared.Configuration;
using PerformanceLab.Application.Users.Abstractions;
using PerformanceLab.Application.Users.Models;

namespace PerformanceLab.Application.Users;

public class UserService
{
    private readonly IUserRepository _repo;
    private readonly PerformanceFeatures _perfFeatures;

    public UserService(IUserRepository repo, PerformanceFeatures perfFeatures)
    {
        _repo = repo;
        _perfFeatures = perfFeatures;
    }

    public IEnumerable<UserDto> GetUsers(int? offset = null, int? limit = null)
    {
        if (_perfFeatures.EnableObjectPooling)
        {
            var pooledUsers = GetUsersWithPooling(offset, limit);
            
            if (_perfFeatures.EnableStreaming)
            {
                // Return as-is for streaming
                // Note: Disposal must happen after serialization completes (handled by controller or GC)
                return pooledUsers;
            }
            else
            {
                // Materialize to list and dispose the pooled collection immediately
                var list = pooledUsers.ToList();
                pooledUsers.Dispose();
                return list;
            }
        }
        else
        {
            // LINQ approach (baseline)
            var users = GetUsersWithLinq(offset, limit);
            
            // Conditionally materialize based on EnableStreaming flag
            return _perfFeatures.EnableStreaming 
                ? users 
                : users.ToList();
        }
    }

    public int GetCount()
    {
        return _repo.GetCount();
    }

    private PooledUserDtoCollection GetUsersWithPooling(int? offset = null, int? limit = null)
    {
        // Get users - paginated or all
        var users = limit.HasValue 
            ? _repo.GetPage(offset ?? 0, limit.Value) 
            : _repo.GetAll();
        var count = users.Count;
        
        // Rent array from pool
        var dtoArray = ArrayPool<UserDto>.Shared.Rent(count);
        
        // Populate DTOs
        for (int i = 0; i < count; i++)
        {
            var user = users[i];
            dtoArray[i] = new UserDto
            {
                Id = user.Id,
                Name = user.Name
            };
        }
        
        // Wrap in disposable collection
        return new PooledUserDtoCollection(dtoArray, count);
    }

    private IEnumerable<UserDto> GetUsersWithLinq(int? offset = null, int? limit = null)
    {
        // Get users - paginated or all
        var users = limit.HasValue 
            ? _repo.GetPage(offset ?? 0, limit.Value) 
            : _repo.GetAll();
            
        return users
            .Select(u => new UserDto
            {
                Id = u.Id,
                Name = u.Name
            });
    }
}