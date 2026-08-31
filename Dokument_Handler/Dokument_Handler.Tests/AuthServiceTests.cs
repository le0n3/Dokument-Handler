using Dokument_Handler.Services;
using Microsoft.Extensions.Configuration;

namespace Dokument_Handler.Tests;

public class AuthServiceTests
{
    [Fact]
    public void VerifyPassword_AcceptsPbkdf2HashAndRejectsWrongPassword()
    {
        const string password = "a-long-test-password";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:PasswordHash"] = AuthService.HashPassword(password)
            })
            .Build();
        var sut = new AuthService(configuration);

        Assert.True(sut.VerifyPassword(password));
        Assert.False(sut.VerifyPassword("wrong"));
    }

    [Fact]
    public void VerifyPassword_WhenNotConfigured_DeniesLogin()
    {
        var configuration = new ConfigurationBuilder().Build();
        var sut = new AuthService(configuration);

        Assert.False(sut.VerifyPassword("admin"));
    }
}
