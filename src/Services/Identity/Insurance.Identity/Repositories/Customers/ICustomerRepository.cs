using Insurance.Identity.Contracts;

namespace Insurance.Identity.Repositories;

// Customers only: every method returns null when the id isn't a customer (admins are never reachable here).
public interface ICustomerRepository
{
    Task<List<CustomerInfo>> GetAllAsync(string? search, bool? isActive);
    Task<CustomerInfo?> GetAsync(int userId);
    Task<CustomerInfo?> SetActiveAsync(int userId, bool isActive);
    Task<CustomerInfo?> UpdateDetailsAsync(int userId, string fullName, string? phone, string? address);

    // The one-time password's hash replaces the password; the customer must choose a new one before expiresAtUtc.
    Task<CustomerInfo?> ResetPasswordAsync(int userId, string passwordHash, DateTime expiresAtUtc);
}
