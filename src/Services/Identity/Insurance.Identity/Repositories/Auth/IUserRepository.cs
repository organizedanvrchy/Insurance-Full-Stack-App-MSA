using Insurance.Identity.Contracts;
using Insurance.Identity.Models;

namespace Insurance.Identity.Repositories;

public interface IUserRepository
{
    Task<int> CreateAsync(User user);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByIdAsync(int userId);
    Task<List<UserAccount>> GetManyAsync(IEnumerable<int> userIds);

    // The new email is unverified until its link is used. Returns the old email. Throws DuplicateEmailException.
    Task<string> ChangeEmailAsync(int userId, string newEmail);
    Task ChangePasswordAsync(int userId, string passwordHash);
}
