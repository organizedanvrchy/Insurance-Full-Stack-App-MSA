using System.Net.Http.Json;
using Insurance.BuildingBlocks.Http;
using Insurance.BuildingBlocks.Security;

namespace Insurance.Identity.Contracts;

/// <summary>Read access to user accounts for other services and the gateway. Never exposes the password hash.</summary>
public interface IUserDirectory
{
    Task<UserAccount?> GetAsync(int userId);   // null => no such user
    Task<IReadOnlyDictionary<int, UserAccount>> GetManyAsync(IEnumerable<int> userIds);
}

/// <summary>
/// EmailVerified: the customer has confirmed they own Email. Enrolling and filing claims require it.
/// MustChangePassword: an admin reset the password; until a new one is chosen, the gateway allows nothing else.
/// </summary>
public record UserAccount(int UserId, string FullName, string Email, UserRole Role, bool IsActive, bool EmailVerified,
    bool MustChangePassword = false);

/// <summary>Customer administration, used by the gateway's BFF (which adds policy and claim counts).</summary>
public interface ICustomerDirectory
{
    Task<List<CustomerInfo>> GetAllAsync(string? search, bool? isActive);
    Task<CustomerInfo> GetCustomerAsync(int userId);                             // NotFoundException if not a customer
    Task<CustomerInfo> SetActiveAsync(int userId, bool isActive);                // NotFoundException if not a customer

    // 400 invalid details, 404 not a customer, 409 email taken. A new email must be verified again.
    Task<CustomerInfo> UpdateDetailsAsync(int userId, CustomerDetailsUpdate update);

    // Emails the customer a one-time password (30 minutes); the old password stops working at once.
    Task<CustomerInfo> ResetPasswordAsync(int userId);
}

/// <summary>
/// A customer as the admin screens show it. Address is one line ("48 Maple Avenue, Apt 3B, Stamford, CT 06902").
/// PasswordResetPending: an admin reset the password and the customer hasn't chosen a new one yet.
/// </summary>
public record CustomerInfo(int UserId, string FullName, string Email, string? Phone, bool IsActive, DateTime CreatedAt,
    bool EmailVerified, string? Address = null, bool PasswordResetPending = false);

/// <summary>What an admin can change on a customer: the same fields as sign-up, except the password.</summary>
public record CustomerDetailsUpdate(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Street,
    string? Unit,
    string City,
    string State,
    string PostalCode);

/// <summary>Calls /internal/users and /internal/customers on the Identity service.</summary>
public class IdentityClient : IUserDirectory, ICustomerDirectory
{
    private readonly HttpClient _http;
    public IdentityClient(HttpClient http) => _http = http;

    public async Task<UserAccount?> GetAsync(int userId) =>
        (await GetManyAsync(new[] { userId })).GetValueOrDefault(userId);

    public async Task<IReadOnlyDictionary<int, UserAccount>> GetManyAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, UserAccount>();

        var response = await _http.GetAsync($"internal/users?{ServiceClients.IdQuery("ids", ids)}");
        return (await response.ReadOrThrowAsync<List<UserAccount>>()).ToDictionary(u => u.UserId);
    }

    public async Task<List<CustomerInfo>> GetAllAsync(string? search, bool? isActive)
    {
        var query = new List<string>();
        if (search is not null) query.Add($"search={Uri.EscapeDataString(search)}");
        if (isActive is not null) query.Add($"isActive={isActive.Value.ToString().ToLowerInvariant()}");

        var response = await _http.GetAsync("internal/customers" + (query.Count > 0 ? "?" + string.Join("&", query) : ""));
        return await response.ReadOrThrowAsync<List<CustomerInfo>>();
    }

    public async Task<CustomerInfo> GetCustomerAsync(int userId) =>
        await (await _http.GetAsync($"internal/customers/{userId}")).ReadOrThrowAsync<CustomerInfo>();

    public async Task<CustomerInfo> SetActiveAsync(int userId, bool isActive)
    {
        var response = await _http.PutAsJsonAsync($"internal/customers/{userId}/active", new { isActive }, ServiceClients.Json);
        return await response.ReadOrThrowAsync<CustomerInfo>();
    }

    public async Task<CustomerInfo> UpdateDetailsAsync(int userId, CustomerDetailsUpdate update)
    {
        var response = await _http.PutAsJsonAsync($"internal/customers/{userId}", update, ServiceClients.Json);
        return await response.ReadOrThrowAsync<CustomerInfo>();
    }

    public async Task<CustomerInfo> ResetPasswordAsync(int userId)
    {
        var response = await _http.PostAsync($"internal/customers/{userId}/reset-password", null);
        return await response.ReadOrThrowAsync<CustomerInfo>();
    }
}
