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

    public List<UserDto> GetUsers()
    {
        return _repo.GetAll()
            .Select(u => new UserDto
            {
                Id = u.Id,
                Name = u.Name
            })
            .ToList();
    }
}