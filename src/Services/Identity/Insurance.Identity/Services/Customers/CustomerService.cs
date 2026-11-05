using Insurance.BuildingBlocks.Exceptions;
using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;
using Insurance.Identity.Messaging;
using Insurance.Identity.Repositories;
using Insurance.Identity.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Insurance.Identity.Services;

// Policy and claim counts belong to other services; the gateway's BFF adds them to the admin screen.
public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly IEmailVerificationService _verification;
    private readonly IIdentityEvents _events;
    private readonly CustomerOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(ICustomerRepository customers, IEmailVerificationService verification, IIdentityEvents events,
        IOptions<CustomerOptions> options, TimeProvider clock, ILogger<CustomerService> logger)
    {
        _customers = customers;
        _verification = verification;
        _events = events;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public Task<List<CustomerInfo>> GetAllAsync(string? search, bool? isActive)
    {
        // Treat blank search as "no search".
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        return _customers.GetAllAsync(term, isActive);
    }

    public async Task<CustomerInfo> GetAsync(int userId) => await _customers.GetAsync(userId) ?? throw NotACustomer();

    public async Task<CustomerInfo> SetActiveAsync(int userId, bool isActive)
    {
        var before = await GetAsync(userId);   // 404 for admins and unknown ids
        var customer = await _customers.SetActiveAsync(userId, isActive) ?? throw NotACustomer();

        // Tell the customer, once: only when the account actually changes. The change stands even if the event can't
        // be published.
        var now = _clock.GetUtcNow().UtcDateTime;
        if (before.IsActive && !customer.IsActive)
            await PublishQuietlyAsync(new AccountDeactivated(customer.UserId, customer.FullName, customer.Email, now));
        else if (!before.IsActive && customer.IsActive)
            await PublishQuietlyAsync(new AccountReactivated(customer.UserId, customer.FullName, customer.Email, now));
        return customer;
    }

    public async Task<CustomerInfo> UpdateDetailsAsync(int userId, AdminUpdateCustomerRequest r)
    {
        var current = await GetAsync(userId);

        // The email first: if the address is taken (409), nothing else is saved either.
        var email = r.Email.Trim().ToLowerInvariant();
        if (email != current.Email)
            await _verification.ChangeEmailByAdminAsync(userId, email);

        return await _customers.UpdateDetailsAsync(userId,
            ContactFormatter.FullName(r.FirstName, r.LastName),
            PhoneNumbers.TryNormalize(r.Phone, out var phone) ? phone : null,
            ContactFormatter.Address(r.Street, r.Unit, r.City, r.State, r.PostalCode)) ?? throw NotACustomer();
    }

    public async Task<CustomerInfo> ResetPasswordAsync(int userId)
    {
        await GetAsync(userId);   // 404 for admins and unknown ids, before anything changes

        var oneTimePassword = OneTimePasswords.New();
        var expires = _clock.GetUtcNow().UtcDateTime.Add(_options.OneTimePasswordLifetime);
        var customer = await _customers.ResetPasswordAsync(userId, BCrypt.Net.BCrypt.HashPassword(oneTimePassword), expires)
            ?? throw NotACustomer();

        // The one-time password only exists in this event and the email it becomes; the database keeps its hash.
        // If it can't be published, the admin sees an error and presses "Reset password" again.
        await _events.PublishAsync(new PasswordResetByAdmin(customer.UserId, customer.FullName, customer.Email,
            oneTimePassword, expires, (int)Math.Round(_options.OneTimePasswordLifetime.TotalMinutes)));
        _logger.LogInformation("An administrator reset the password of customer {UserId}.", userId);
        return customer;
    }

    // Typed, so the event goes out as its own type (MassTransit routes by the compile-time type).
    private async Task PublishQuietlyAsync<T>(T message) where T : class
    {
        try
        {
            await _events.PublishAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish {Event}.", typeof(T).Name);
        }
    }

    private static NotFoundException NotACustomer() => new("Customer not found.");   // a 404
}
