using System.Security.Cryptography;
using System.Text;

namespace WorldAlerts.Api.Admin;

/// <summary>PBKDF2-SHA256 hash format: iterations:base64-salt:base64-derived-key. Never store plaintext in configuration.</summary>
public static class AdminPassword
{
    private const int Iterations = 310_000;
    // Deliberate local-development-only default. Never put a plaintext password
    // or a generated-at-startup value in the application configuration.
    internal const string DevelopmentHash = "310000:jwwH10vKGLyjAnG7nzlVZb8W5L5tDsu3mCFyPYPPYdk=:JwVdeaL7dujISNljEpW/duFs5rxWAFOFKiAL/VAPV6w=";

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(32);
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(derived)}";
    }

    public static bool Verify(string? password, string? encoded)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 1024 || string.IsNullOrWhiteSpace(encoded)) return false;
        try
        {
            var parts = encoded.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations < 100_000 || iterations > 2_000_000)
                return false;
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            if (salt.Length != 32 || expected.Length != 32) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
