using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace Dokument_Handler.Services;

/// <summary>Holds the configured path for the document storage root directory.</summary>
public class StorageOptions
{
    /// <summary>Gets or sets the absolute path to the root storage directory. Empty means use the application default.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum total size of stored documents. Zero disables the quota.</summary>
    public long MaxTotalSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024;
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
    private readonly IDataProtector? _secretProtector;

    public AppSettingsService(IWebHostEnvironment env, IDataProtectionProvider? dataProtectionProvider = null)
    {
        _env = env;
        _secretProtector = dataProtectionProvider?.CreateProtector("DokumentHandler.Settings.v1");
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
            var settings = new AppSettingsSnapshot
            {
                Storage = ReadSection<StorageOptions>(root, "Storage") ?? new StorageOptions(),
                EmailImport = ReadSection<EmailImportOptions>(root, "EmailImport") ?? new EmailImportOptions(),
                AiClassification = ReadSection<AiClassificationOptions>(root, "AiClassification") ?? new AiClassificationOptions()
            };

            settings.EmailImport.Password = Environment.GetEnvironmentVariable("DOKUMENT_HANDLER_IMAP_PASSWORD")
                                            ?? Unprotect(settings.EmailImport.Password);
            settings.AiClassification.ApiKey = Environment.GetEnvironmentVariable("DOKUMENT_HANDLER_AI_API_KEY")
                                               ?? Unprotect(settings.AiClassification.ApiKey);
            return settings;
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
            var existingEmailPassword = root["EmailImport"]?[nameof(EmailImportOptions.Password)]?.GetValue<string>()
                                        ?? string.Empty;
            var existingAiApiKey = root["AiClassification"]?[nameof(AiClassificationOptions.ApiKey)]?.GetValue<string>()
                                   ?? string.Empty;
            var emailImport = JsonSerializer.SerializeToNode(settings.EmailImport, _jsonOptions)!.AsObject();
            var aiClassification = JsonSerializer.SerializeToNode(settings.AiClassification, _jsonOptions)!.AsObject();
            emailImport[nameof(EmailImportOptions.Password)] =
                Environment.GetEnvironmentVariable("DOKUMENT_HANDLER_IMAP_PASSWORD") == null
                    ? Protect(settings.EmailImport.Password)
                    : existingEmailPassword;
            aiClassification[nameof(AiClassificationOptions.ApiKey)] =
                Environment.GetEnvironmentVariable("DOKUMENT_HANDLER_AI_API_KEY") == null
                    ? Protect(settings.AiClassification.ApiKey)
                    : existingAiApiKey;

            root["Storage"] = JsonSerializer.SerializeToNode(settings.Storage, _jsonOptions);
            root["EmailImport"] = emailImport;
            root["AiClassification"] = aiClassification;

            var tempPath = SettingsPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, root.ToJsonString(_jsonOptions));
                if (File.Exists(SettingsPath)) File.Copy(SettingsPath, SettingsPath + ".bak", true);
                File.Move(tempPath, SettingsPath, true);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
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

    private string Protect(string value)
    {
        if (string.IsNullOrEmpty(value) || _secretProtector == null) return value;
        return "protected:" + _secretProtector.Protect(value);
    }

    private string Unprotect(string value)
    {
        if (!value.StartsWith("protected:", StringComparison.Ordinal) || _secretProtector == null)
            return value;

        try
        {
            return _secretProtector.Unprotect(value[10..]);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return string.Empty;
        }
    }
}
