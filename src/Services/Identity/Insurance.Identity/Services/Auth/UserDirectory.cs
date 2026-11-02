using Insurance.Identity.Contracts;
using Insurance.Identity.Repositories;

namespace Insurance.Identity.Services;

/// <summary>Implements the service's internal API on top of its own repository.</summary>
public class UserDirectory : IUserDirectory
{
    private readonly IUserRepository _users;
    public UserDirectory(IUserRepository users) => _users = users;

    public async Task<UserAccount?> GetAsync(int userId)
    {
        var u = await _users.GetByIdAsync(userId);
        return u is null ? null : new UserAccount(u.UserId, u.FullName, u.Email, u.Role, u.IsActive, u.EmailVerified);
    }

    public async Task<IReadOnlyDictionary<int, UserAccount>> GetManyAsync(IEnumerable<int> userIds) =>
        (await _users.GetManyAsync(userIds)).ToDictionary(u => u.UserId);
}
