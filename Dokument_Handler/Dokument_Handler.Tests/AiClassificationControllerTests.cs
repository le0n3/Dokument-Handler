using Dokument_Handler.Controllers;
using Dokument_Handler.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dokument_Handler.Tests;

public class AiClassificationControllerTests
{
    [Fact]
    public void GetStatus_ReturnsEnabledFalseWhenServiceDisabled()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteSettings(new
        {
            AiClassification = new
            {
                Enabled = false,
                ApiKey = "test"
            }
        });

        var docService = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        var aiService = new AiClassificationService(new HttpClient(), workspace.AppSettingsService, NullLogger<AiClassificationService>.Instance);
        var sut = new AiClassificationController(docService, aiService);

        var result = sut.GetStatus();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("False", ok.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClassifyOne_WhenDisabled_ReturnsBadRequest()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteSettings(new
        {
            AiClassification = new
            {
                Enabled = false,
                ApiKey = "test"
            }
        });

        var docService = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        var aiService = new AiClassificationService(new HttpClient(), workspace.AppSettingsService, NullLogger<AiClassificationService>.Instance);
        var sut = new AiClassificationController(docService, aiService);

        var result = await sut.ClassifyOne(Guid.NewGuid());

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ClassifyAll_WhenDisabled_ReturnsBadRequest()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteSettings(new
        {
            AiClassification = new
            {
                Enabled = false,
                ApiKey = "test"
            }
        });

        var docService = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        var aiService = new AiClassificationService(new HttpClient(), workspace.AppSettingsService, NullLogger<AiClassificationService>.Instance);
        var sut = new AiClassificationController(docService, aiService);

        var result = await sut.ClassifyAll();

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
