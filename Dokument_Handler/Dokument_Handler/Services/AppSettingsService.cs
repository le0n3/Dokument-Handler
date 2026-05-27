using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dokument_Handler.Services;

public class StorageOptions
{
    public string RootPath { get; set; } = string.Empty;
}

public class AppSettingsSnapshot
{
    public StorageOptions Storage { get; set; } = new();
    public EmailImportOptions EmailImport { get; set; } = new();
    public AiClassificationOptions AiClassification { get; set; } = new();
}

public class AppSettingsService
{
    private readonly IWebHostEnvironment _env;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();

    public AppSettingsService(IWebHostEnvironment env)
    {
        _env = env;
    }

    private string SettingsPath => Path.Combine(_env.ContentRootPath, "appsettings.json");

    public AppSettingsSnapshot GetSettings()
    {
        lock (_sync)
        {
            var root = ReadRoot();
            return new AppSettingsSnapshot
            {
                Storage = ReadSection<StorageOptions>(root, "Storage") ?? new StorageOptions(),
                EmailImport = ReadSection<EmailImportOptions>(root, "EmailImport") ?? new EmailImportOptions(),
                AiClassification = ReadSection<AiClassificationOptions>(root, "AiClassification") ?? new AiClassificationOptions()
            };
        }
    }

    public StorageOptions GetStorageOptions() => GetSettings().Storage;

    public EmailImportOptions GetEmailImportOptions() => GetSettings().EmailImport;

    public AiClassificationOptions GetAiClassificationOptions() => GetSettings().AiClassification;

    public Task SaveSettingsAsync(AppSettingsSnapshot settings)
    {
        lock (_sync)
        {
            var root = ReadRoot();
            root["Storage"] = JsonSerializer.SerializeToNode(settings.Storage, _jsonOptions);
            root["EmailImport"] = JsonSerializer.SerializeToNode(settings.EmailImport, _jsonOptions);
            root["AiClassification"] = JsonSerializer.SerializeToNode(settings.AiClassification, _jsonOptions);

            File.WriteAllText(SettingsPath, root.ToJsonString(_jsonOptions));
        }

        return Task.CompletedTask;
    }

    private JsonObject ReadRoot()
    {
        if (!File.Exists(SettingsPath))
            return new JsonObject();

        var json = File.ReadAllText(SettingsPath);
        return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
    }

    private static T? ReadSection<T>(JsonObject root, string sectionName)
    {
        var node = root[sectionName];
        return node == null ? default : node.Deserialize<T>();
    }
}
