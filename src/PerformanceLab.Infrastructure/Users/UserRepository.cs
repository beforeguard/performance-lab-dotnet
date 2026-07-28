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

    public IReadOnlyList<User> GetPage(int offset, int limit)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be greater than or equal to 0.");
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0.");
        }

        return _users.Skip(offset).Take(limit).ToList();
    }

    public int GetCount()
    {
        return _users.Count;
    }
}