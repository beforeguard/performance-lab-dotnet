using PerformanceLab.Application.Users.Abstractions;
using PerformanceLab.Domain.Users;

namespace PerformanceLab.Infrastructure.Users;

public class UserRepository : IUserRepository
{
    private readonly List<User> _users;

    public UserRepository()
    {
        _users = Enumerable.Range(1, 10_000)
            .Select(i => new User
            {
                Id = i,
                Name = $"User {i}",
                Email = $"user{i}@test.com",
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            })
            .ToList();
    }

    public IReadOnlyList<User> GetAll()
    {
        return _users;
    }
}