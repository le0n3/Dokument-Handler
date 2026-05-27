using Dokument_Handler.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dokument_Handler.Tests;

public class AiClassificationServiceTests
{
    [Fact]
    public void IsEnabled_FalseWhenApiKeyIsPlaceholder()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteSettings(new
        {
            AiClassification = new
            {
                Enabled = true,
                ApiKey = "YOUR_API_KEY"
            }
        });

        var sut = new AiClassificationService(new HttpClient(), workspace.AppSettingsService, NullLogger<AiClassificationService>.Instance);

        Assert.False(sut.IsEnabled);
    }

    [Fact]
    public void IsEnabled_TrueWhenEnabledAndApiKeyIsSet()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteSettings(new
        {
            AiClassification = new
            {
                Enabled = true,
                ApiKey = "local-key"
            }
        });

        var sut = new AiClassificationService(new HttpClient(), workspace.AppSettingsService, NullLogger<AiClassificationService>.Instance);

        Assert.True(sut.IsEnabled);
    }

    [Fact]
    public async Task SuggestDocumentNameAsync_WhenDisabled_ReturnsNull()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteSettings(new
        {
            AiClassification = new
            {
                Enabled = false,
                ApiKey = "local-key"
            }
        });
        var sut = new AiClassificationService(new HttpClient(), workspace.AppSettingsService, NullLogger<AiClassificationService>.Instance);

        var result = await sut.SuggestDocumentNameAsync("missing-file.pdf");

        Assert.Null(result);
    }

    [Fact]
    public async Task ClassifyDocumentAsync_WhenDisabled_ReturnsNull()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteSettings(new
        {
            AiClassification = new
            {
                Enabled = false,
                ApiKey = "local-key"
            }
        });
        var sut = new AiClassificationService(new HttpClient(), workspace.AppSettingsService, NullLogger<AiClassificationService>.Instance);

        var result = await sut.ClassifyDocumentAsync("missing-file.pdf", ["Allgemein"]);

        Assert.Null(result);
    }
}
