using System.Security.Cryptography;

namespace Insurance.Identity.Services;

/// <summary>
/// The password an admin reset emails to a customer: 14 random characters with an upper, lower, digit and symbol, so
/// it passes the password rules, and without look-alikes (0/O, 1/l/I) so it's easy to type from an email.
/// RandomNumberGenerator is cryptographically secure, so it can't be predicted.
/// </summary>
public static class OneTimePasswords
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%&*?";

    public static string New(int length = 14)
    {
        const string all = Upper + Lower + Digits + Symbols;
        var chars = new List<char>
        {
            Upper[RandomNumberGenerator.GetInt32(Upper.Length)],
            Lower[RandomNumberGenerator.GetInt32(Lower.Length)],
            Digits[RandomNumberGenerator.GetInt32(Digits.Length)],
            Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)]
        };
        while (chars.Count < length)
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

        // Shuffle, so the guaranteed characters aren't always first.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }
}
