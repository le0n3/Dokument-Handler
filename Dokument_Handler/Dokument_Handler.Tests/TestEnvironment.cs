using System.Text.Json;
using Dokument_Handler.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Dokument_Handler.Tests;

internal sealed class TestEnvironment(string rootPath) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Dokument_Handler.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = rootPath;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = rootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class TestWorkspace : IDisposable
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "DokumentHandlerTests", Guid.NewGuid().ToString("N"));
    public TestEnvironment Environment { get; }
    public AppSettingsService AppSettingsService { get; }

    public TestWorkspace()
    {
        Directory.CreateDirectory(RootPath);
        Environment = new TestEnvironment(RootPath);
        AppSettingsService = new AppSettingsService(Environment);
    }

    public void WriteSettings(object settings)
    {
        var path = Path.Combine(RootPath, "appsettings.json");
        var json = JsonSerializer.Serialize(settings);
        File.WriteAllText(path, json);
    }

    public void WriteSettings(AppSettingsSnapshot settings)
    {
        WriteSettings(new
        {
            Storage = settings.Storage,
            EmailImport = settings.EmailImport,
            AiClassification = settings.AiClassification
        });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }
        }
        catch
        {
            // ignore cleanup issues in tests
        }
    }
}
