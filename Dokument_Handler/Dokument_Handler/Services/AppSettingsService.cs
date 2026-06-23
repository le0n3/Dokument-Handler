using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dokument_Handler.Services;

/// <summary>Holds the configured path for the document storage root directory.</summary>
public class StorageOptions
{
    /// <summary>Gets or sets the absolute path to the root storage directory. Empty means use the application default.</summary>
    public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// A snapshot of all configurable application settings, read from or written to <c>appsettings.json</c>.
/// </summary>
public class AppSettingsSnapshot
{
    /// <summary>Gets or sets the document storage configuration.</summary>
    public StorageOptions Storage { get; set; } = new();

    /// <summary>Gets or sets the IMAP email import configuration.</summary>
    public EmailImportOptions EmailImport { get; set; } = new();

    /// <summary>Gets or sets the AI classification configuration.</summary>
    public AiClassificationOptions AiClassification { get; set; } = new();
}

/// <summary>
/// Reads and writes application settings from <c>appsettings.json</c> at runtime.
/// All reads and writes are thread-safe.
/// </summary>
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

    /// <summary>
    /// Reads and returns all settings sections from <c>appsettings.json</c>.
    /// Missing sections are returned with their default values.
    /// </summary>
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

    /// <summary>Returns the storage configuration section.</summary>
    public StorageOptions GetStorageOptions() => GetSettings().Storage;

    /// <summary>Returns the email import configuration section.</summary>
    public EmailImportOptions GetEmailImportOptions() => GetSettings().EmailImport;

    /// <summary>Returns the AI classification configuration section.</summary>
    public AiClassificationOptions GetAiClassificationOptions() => GetSettings().AiClassification;

    /// <summary>
    /// Persists all sections of <paramref name="settings"/> to <c>appsettings.json</c>,
    /// preserving any unrelated keys already present in the file.
    /// </summary>
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
