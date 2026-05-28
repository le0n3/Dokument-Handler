using System.Security.Cryptography;
using System.Text;

namespace Dokument_Handler.Services;

public class AuthService
{
    private readonly IConfiguration _configuration;
    private bool _isAuthenticated;

    public AuthService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsAuthenticated => _isAuthenticated;

    public bool Login(string password)
    {
        var storedHash = _configuration["AppPassword"] ?? string.Empty;
        var inputHash = ComputeSha256Hash(password);
        _isAuthenticated = string.Equals(storedHash, inputHash, StringComparison.OrdinalIgnoreCase);
        return _isAuthenticated;
    }

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
