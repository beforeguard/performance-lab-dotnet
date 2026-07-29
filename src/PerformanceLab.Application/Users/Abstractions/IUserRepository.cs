using PerformanceLab.Domain.Users;

namespace PerformanceLab.Application.Users.Abstractions;

public interface IUserRepository
{
    IReadOnlyList<User> GetAll();
    IReadOnlyList<User> GetPage(int offset, int limit);
    int GetCount();
}