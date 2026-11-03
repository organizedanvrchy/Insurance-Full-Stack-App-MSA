using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;

namespace Insurance.Identity.Services;

// What an admin does to customer accounts. Every method throws NotFoundException if the id isn't a customer.
public interface ICustomerService
{
    Task<List<CustomerInfo>> GetAllAsync(string? search, bool? isActive);
    Task<CustomerInfo> GetAsync(int userId);
    Task<CustomerInfo> SetActiveAsync(int userId, bool isActive);

    // Name, phone and address are saved as given. A new email works at once but must be verified again; the old
    // address is told an administrator changed it. 409 if another account uses the email.
    Task<CustomerInfo> UpdateDetailsAsync(int userId, AdminUpdateCustomerRequest request);

    // Emails a one-time password (CustomerOptions.OneTimePasswordLifetime); the old password stops working at once.
    Task<CustomerInfo> ResetPasswordAsync(int userId);
}

/// <summary>"Customers" section of the configuration.</summary>
public class CustomerOptions
{
    /// <summary>How long the one-time password from an admin reset works.</summary>
    public TimeSpan OneTimePasswordLifetime { get; set; } = TimeSpan.FromMinutes(30);
}
