using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Insurance.Identity.Services;

/// <summary>
/// The secret in a verification link. 32 random bytes (256 bits), URL-safe. The database only ever sees the
/// SHA-256 hash, so reading the table doesn't let anyone verify an address. A fast hash is fine here (unlike for
/// passwords): the token is random and long, so there is nothing to guess.
/// </summary>
public static class VerificationTokens
{
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
}
