using System.Security.Cryptography;
using System.Text;

namespace Dokument_Handler.Services;

/// <summary>
/// Manages application-level authentication via a single shared password.
/// The configured password is stored as a SHA-256 hex hash in <c>appsettings.json</c>.
/// </summary>
public class AuthService
{
    private readonly IConfiguration _configuration;
    private bool _isAuthenticated;

    public AuthService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Gets a value indicating whether the current session is authenticated.</summary>
    public bool IsAuthenticated => _isAuthenticated;

    /// <summary>
    /// Attempts to authenticate using the given <paramref name="password"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the password matches; otherwise <see langword="false"/>.</returns>
    public bool Login(string password)
    {
        var storedHash = _configuration["AppPassword"] ?? string.Empty;
        var inputHash = ComputeSha256Hash(password);
        _isAuthenticated = string.Equals(storedHash, inputHash, StringComparison.OrdinalIgnoreCase);
        return _isAuthenticated;
    }

    /// <summary>Ends the current authenticated session.</summary>
    public void Logout()
    {
        _isAuthenticated = false;
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
