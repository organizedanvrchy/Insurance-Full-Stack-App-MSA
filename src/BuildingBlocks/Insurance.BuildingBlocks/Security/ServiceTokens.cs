using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Insurance.BuildingBlocks.Security;

/// <summary>
/// A short-lived JWT that identifies a SERVICE rather than a person. Calls between services normally forward the
/// signed-in user's token, but background work (a message consumer, the payment reminder job) has no user. Those
/// calls carry this token instead, so the internal endpoints, which only require an authenticated caller, accept them.
///
/// It is signed with the same key as user tokens and carries the "Service" role. It never has a user id, so it can't
/// pass for a customer or an admin on any public endpoint, and the gateway rejects it (no numeric user id).
/// </summary>
public interface IServiceTokenProvider
{
    string GetToken();
}

public class ServiceTokenProvider : IServiceTokenProvider
{
    public const string Role = "Service";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IConfiguration _config;
    private readonly string _serviceName;
    private readonly object _lock = new();
    private (string Token, DateTime ExpiresAtUtc)? _cached;

    public ServiceTokenProvider(IConfiguration config, IHostEnvironment env)
    {
        _config = config;
        _serviceName = env.ApplicationName;
    }

    public string GetToken()
    {
        lock (_lock)
        {
            // Renew a minute before expiry, so a token never expires in flight.
            if (_cached is { } c && c.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
                return c.Token;

            var expires = DateTime.UtcNow.Add(Lifetime);
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, _serviceName),
                    new Claim(ClaimTypes.Role, Role)
                }),
                Issuer = _config["Jwt:Issuer"],
                Audience = _config["Jwt:Audience"],
                Expires = expires,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
            });

            _cached = (token, expires);
            return token;
        }
    }
}
