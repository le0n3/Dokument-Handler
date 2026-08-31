using System.Security.Cryptography;
using System.Text;

namespace Dokument_Handler.Services;

/// <summary>Verifies the single-user application password without keeping global session state.</summary>
public sealed class AuthService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;
    private readonly IConfiguration _configuration;

    public AuthService(IConfiguration configuration) => _configuration = configuration;

    public bool VerifyPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return false;

        var environmentPassword = Environment.GetEnvironmentVariable("DOKUMENT_HANDLER_PASSWORD");
        if (!string.IsNullOrEmpty(environmentPassword))
            return FixedTimeEquals(password, environmentPassword);

        var encodedHash = _configuration["Authentication:PasswordHash"];
        if (string.IsNullOrWhiteSpace(encodedHash)) return false;

        var parts = encodedHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)
            || iterations is < 100_000 or > 1_000_000)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
}
