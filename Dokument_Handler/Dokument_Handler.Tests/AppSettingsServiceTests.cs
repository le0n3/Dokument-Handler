using Dokument_Handler.Services;

namespace Dokument_Handler.Tests;

public class AppSettingsServiceTests
{
    [Fact]
    public void GetSettings_WhenFileMissing_ReturnsDefaults()
    {
        using var workspace = new TestWorkspace();

        var settings = workspace.AppSettingsService.GetSettings();

        Assert.NotNull(settings.Storage);
        Assert.NotNull(settings.EmailImport);
        Assert.NotNull(settings.AiClassification);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsUpdatedValues()
    {
        using var workspace = new TestWorkspace();
        var newSettings = new AppSettingsSnapshot
        {
            Storage = new StorageOptions
            {
                RootPath = Path.Combine(workspace.RootPath, "custom-storage")
            },
            EmailImport = new EmailImportOptions
            {
                Enabled = true,
                Host = "imap.example.com",
                Username = "user",
                Password = "secret",
                Mailbox = "INBOX",
                PollIntervalSeconds = 30
            },
            AiClassification = new AiClassificationOptions
            {
                Enabled = true,
                ApiBaseUrl = "http://localhost:11434/v1",
                ApiKey = "local-key",
                Model = "llama3.2"
            }
        };

        await workspace.AppSettingsService.SaveSettingsAsync(newSettings);
        var reloaded = workspace.AppSettingsService.GetSettings();

        Assert.Equal(newSettings.Storage.RootPath, reloaded.Storage.RootPath);
        Assert.True(reloaded.EmailImport.Enabled);
        Assert.Equal("imap.example.com", reloaded.EmailImport.Host);
        Assert.Equal(30, reloaded.EmailImport.PollIntervalSeconds);
        Assert.Equal("local-key", reloaded.AiClassification.ApiKey);
    }
}
