using Insurance.Identity.Contracts;
using Insurance.Identity.Models;

namespace Insurance.Identity.Repositories;

public interface IUserRepository
{
    Task<int> CreateAsync(User user);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByIdAsync(int userId);
    Task<List<UserAccount>> GetManyAsync(IEnumerable<int> userIds);
}
