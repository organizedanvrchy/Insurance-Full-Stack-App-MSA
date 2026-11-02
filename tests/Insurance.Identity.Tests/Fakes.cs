using Insurance.Identity.Contracts;
using Insurance.Identity.Messaging;
using Insurance.Identity.Exceptions;
using Insurance.Identity.Models;
using Insurance.Identity.Repositories;
using Insurance.Identity.Services;

namespace Insurance.Identity.Tests;

// Fakes are tiny in-memory stand-ins for the 
// real repositories. The services only know the
// interfaces (IUserRepository and so on), so they 
// can't tell the difference. That's dependency injection 
// paying off: no database, no Azure, tests run in milliseconds.

public class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = new();
    private int _nextId = 1;

    public Task<int> CreateAsync(User user)
    {
        // Mimics what the real repository does when SQL reports a duplicate email.
        if (Users.Any(u => u.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateEmailException(user.Email);

        user.UserId = _nextId++;
        Users.Add(user);
        return Task.FromResult(user.UserId);
    }

    public Task<User?> GetByEmailAsync(string email) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));

    public Task<User?> GetByIdAsync(int userId) =>
        Task.FromResult(Users.FirstOrDefault(u => u.UserId == userId));

    public Task<List<UserAccount>> GetManyAsync(IEnumerable<int> userIds) =>
        Task.FromResult(Users.Where(u => userIds.Contains(u.UserId))
            .Select(u => new UserAccount(u.UserId, u.FullName, u.Email, u.Role, u.IsActive, u.EmailVerified)).ToList());
}

/// <summary>Mimics accounts.usp_EmailToken_* (create, recent count, consume) in memory.</summary>
public class FakeEmailTokenRepository : IEmailTokenRepository
{
    public class Row
    {
        public int UserId;
        public string Email = "";
        public string Hash = "";
        public DateTime CreatedAt;
        public DateTime ExpiresAt;
        public bool Used;
    }

    private readonly FakeUserRepository _users;
    private readonly TimeProvider _clock;
    public List<Row> Rows { get; } = new();

    public FakeEmailTokenRepository(FakeUserRepository users, TimeProvider clock)
    {
        _users = users;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task CreateAsync(int userId, string email, string tokenHash, DateTime expiresAtUtc)
    {
        Rows.Where(r => r.UserId == userId && !r.Used).ToList().ForEach(r => r.Used = true);
        Rows.Add(new Row { UserId = userId, Email = email, Hash = tokenHash, CreatedAt = Now, ExpiresAt = expiresAtUtc });
        return Task.CompletedTask;
    }

    public Task<RecentEmailTokens> GetRecentAsync(int userId)
    {
        var recent = Rows.Where(r => r.UserId == userId && r.CreatedAt > Now.AddHours(-24)).ToList();
        var pending = recent.Where(r => !r.Used).ToList();
        return Task.FromResult(new RecentEmailTokens(pending.Count == 0 ? null : pending.Max(r => r.CreatedAt), recent.Count));
    }

    public Task<User> ConsumeAsync(string tokenHash)
    {
        var row = Rows.FirstOrDefault(r => r.Hash == tokenHash && !r.Used && r.ExpiresAt > Now)
            ?? throw new InvalidVerificationLinkException();
        var user = _users.Users.Single(u => u.UserId == row.UserId);
        if (user.Email != row.Email) throw new InvalidVerificationLinkException();

        user.EmailVerifiedAt ??= Now;
        row.Used = true;
        return Task.FromResult(user);
    }
}

/// <summary>Captures what would have been published to the bus.</summary>
public class FakeIdentityEvents : IIdentityEvents
{
    public List<object> Published { get; } = new();
    public bool Fail { get; set; }   // simulates RabbitMQ being down

    public Task PublishAsync<T>(T message) where T : class
    {
        if (Fail) throw new InvalidOperationException("Broker unreachable");
        Published.Add(message);
        return Task.CompletedTask;
    }

    public IEnumerable<T> OfType<T>() => Published.OfType<T>();
}

/// <summary>A clock the test moves by hand.</summary>
public class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

public class FakeTokenService : ITokenService
{
    public (string Token, DateTime ExpiresAtUtc) CreateToken(User user) =>
        ($"token-for-{user.UserId}", DateTime.UtcNow.AddHours(1));
}
