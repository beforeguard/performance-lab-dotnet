using System.Buffers;
using PerformanceLab.Application.Users.Abstractions;
using PerformanceLab.Application.Users.Models;

namespace PerformanceLab.Application.Users;

public class UserService
{
    private readonly IUserRepository _repo;

    public UserService(IUserRepository repo)
    {
        _repo = repo;
    }

    public PooledUserDtoCollection GetUsers()
    {
        var users = _repo.GetAll();
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
}