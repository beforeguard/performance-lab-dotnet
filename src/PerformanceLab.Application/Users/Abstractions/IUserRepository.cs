using PerformanceLab.Domain.Users;

namespace PerformanceLab.Application.Users.Abstractions;

public interface IUserRepository
{
    IReadOnlyList<User> GetAll();
}