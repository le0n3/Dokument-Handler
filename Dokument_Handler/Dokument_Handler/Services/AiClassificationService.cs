using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dokument_Handler.Models;
using UglyToad.PdfPig;

namespace Dokument_Handler.Services;

/// <summary>
/// Configuration options for the AI document classification feature.
/// Corresponds to the <c>AiClassification</c> section in <c>appsettings.json</c>.
/// </summary>
public class AiClassificationOptions
{
    /// <summary>Gets or sets a value indicating whether AI features are enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the base URL of the OpenAI-compatible API endpoint.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Gets or sets the API key used for authentication.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the model identifier to use for chat completions.</summary>
    public string Model { get; set; } = "llama3.2";

    /// <summary>Gets or sets the maximum number of characters extracted from a PDF before truncation.</summary>
    public int MaxTextChars { get; set; } = 4000;

    /// <summary>
    /// Gets or sets a value indicating whether to request a structured JSON response
    /// from the API (requires model support).
    /// </summary>
    public bool UseJsonFormat { get; set; } = false;
}

/// <summary>The classification result returned by the LLM for a single document.</summary>
public class AiClassificationResult
{
    /// <summary>Gets or sets the assigned category name.</summary>
    public string Category { get; set; } = "Allgemein";

    /// <summary>Gets or sets the list of descriptive tags suggested by the model.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Gets or sets a short description of the document content.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Communicates with an OpenAI-compatible LLM API to classify PDF documents
/// and suggest file names.
/// </summary>
public class AiClassificationService
{
    private readonly HttpClient _http;
    private readonly AppSettingsService _settingsService;
    private readonly ILogger<AiClassificationService> _logger;

    public AiClassificationService(
        HttpClient http,
        AppSettingsService settingsService,
        ILogger<AiClassificationService> logger)
    {
        _http = http;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether AI classification is currently enabled and properly configured.
    /// Returns <see langword="false"/> when the API key is missing or still a placeholder.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            var options = _settingsService.GetAiClassificationOptions();
            return options.Enabled
                   && !string.IsNullOrWhiteSpace(options.ApiKey)
                   && !options.ApiKey.StartsWith("YOUR_");
        }
    }

    /// <summary>
    /// Extracts text from a PDF file and asks the configured LLM to classify it.
    /// Returns <see langword="null"/> when the feature is disabled, no text could be
    /// extracted, or an error occurs.
    /// </summary>
    public async Task<AiClassificationResult?> ClassifyDocumentAsync(
        string filePath,
        IReadOnlyList<string> availableCategories)
    {
        var options = _settingsService.GetAiClassificationOptions();

        if (!IsEnabled)
            return null;

        try
        {
            var text = ExtractPdfText(filePath, options.MaxTextChars);
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogInformation("AI-Klassifizierung: kein Text in {File} gefunden.", filePath);
                return null;
            }

            return await CallLlmAsync(text, availableCategories, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI-Klassifizierung fehlgeschlagen für {File}.", filePath);
            return null;
        }
    }

    /// <summary>
    /// Reads pages from the PDF until <paramref name="maxTextChars"/> characters have been
    /// collected, then truncates the result to that limit.
    /// </summary>
    private string ExtractPdfText(string filePath, int maxTextChars)
    {
        // Enforce a minimum of 500 characters so the LLM has enough context.
        var cappedMax = Math.Max(500, maxTextChars);
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(filePath);
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
            if (sb.Length >= cappedMax) break;
        }
        return sb.Length > cappedMax
            ? sb.ToString()[..cappedMax]
            : sb.ToString();
    }

    /// <summary>
    /// Sends the extracted PDF text to the LLM and parses the structured JSON response
    /// into an <see cref="AiClassificationResult"/>.
    /// </summary>
    private async Task<AiClassificationResult?> CallLlmAsync(
        string pdfText,
        IReadOnlyList<string> availableCategories,
        AiClassificationOptions options)
    {
        var categoriesJson = JsonSerializer.Serialize(availableCategories);
        var systemPrompt =
            "Du bist ein Dokumenten-Klassifizierungs-Assistent für ein privates Dokumentenverwaltungssystem.\n" +
            "Analysiere den bereitgestellten Dokumenttext und antworte AUSSCHLIESSLICH mit einem JSON-Objekt (kein Markdown, kein Text davor oder danach).\n\n" +
            $"Erlaubte Kategorien: {categoriesJson}\n\n" +
            "Antwortformat (exaktes JSON):\n" +
            "{\n" +
            "  \"category\": \"<eine der erlaubten Kategorien>\",\n" +
            "  \"tags\": [\"<tag1>\", \"<tag2>\", \"<tag3>\"],\n" +
            "  \"description\": \"<kurze deutschsprachige Zusammenfassung in 1-2 Sätzen>\"\n" +
            "}\n\n" +
            "Regeln:\n" +
            "- Wähle immer eine Kategorie aus der erlaubten Liste. Bei Unklarheit: \"Allgemein\".\n" +
            "- Tags: 2-5 relevante Stichworte auf Deutsch, Kleinschreibung.\n" +
            "- Beschreibung: prägnant, auf Deutsch.";

        // Build the request body with or without the structured JSON response format hint.
        object requestBody = options.UseJsonFormat
            ? new
            {
                model = options.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Dokumenttext:\n\n{pdfText}" }
                },
                temperature = 0.1,
                max_tokens = 300,
                response_format = new { type = "json_object" }
            }
            : (object)new
            {
                model = options.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Dokumenttext:\n\n{pdfText}" }
                },
                temperature = 0.1,
                max_tokens = 300
            };

        var json = JsonSerializer.Serialize(requestBody);
        using var request = CreateChatRequest(options, json);
        using var response = await _http.SendAsync(request);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var messageContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        // Strip optional Markdown code fences that some models include in their response.
        var jsonMatch = Regex.Match(messageContent, @"```(?:json)?\s*(\{.*?\})\s*```", RegexOptions.Singleline);
        if (jsonMatch.Success)
            messageContent = jsonMatch.Groups[1].Value;
        else if (!messageContent.TrimStart().StartsWith('{'))
        {
            var braceMatch = Regex.Match(messageContent, @"\{.*\}", RegexOptions.Singleline);
            if (braceMatch.Success)
                messageContent = braceMatch.Value;
        }

        return JsonSerializer.Deserialize<AiClassificationResult>(
            messageContent,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    /// <summary>
    /// Extracts text from a PDF at <paramref name="filePath"/> and asks the LLM to
    /// suggest a short, filesystem-safe file name without extension.
    /// Returns <see langword="null"/> when the feature is disabled or an error occurs.
    /// </summary>
    public async Task<string?> SuggestDocumentNameAsync(string filePath)
    {
        var options = _settingsService.GetAiClassificationOptions();

        if (!IsEnabled)
            return null;

        try
        {
            var text = ExtractPdfText(filePath, options.MaxTextChars);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var systemPrompt =
                "Du erzeugst Dateinamen für Dokumente. " +
                "Antworte nur mit einem kurzen Dateinamen ohne Dateiendung (keine Erklärungen). " +
                "Regeln: deutsch, 3-8 Wörter, nur Buchstaben/Zahlen/Leerzeichen/-/_, keine Sonderzeichen wie /:*?\"<>|.";

            var requestBody = new
            {
                model = options.Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = $"Dokumenttext:\n\n{text}" }
                },
                temperature = 0.1,
                max_tokens = 60
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var request = CreateChatRequest(options, json);
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var raw = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            // Sanitize the suggestion: remove whitespace, quotes, and invalid filename characters.
            var name = raw.Trim();
            name = name.Replace("\r", " ").Replace("\n", " ").Trim();
            name = name.Trim('"', '\'', '`');
            name = Regex.Replace(name, "\\s+", " ");

            var invalid = Path.GetInvalidFileNameChars();
            name = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
            name = name.Trim().Trim('.');

            if (name.Length > 80)
                name = name[..80].Trim();

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KI-Dateinamenvorschlag fehlgeschlagen für {File}.", filePath);
            return null;
        }
    }

    private static HttpRequestMessage CreateChatRequest(AiClassificationOptions options, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUri(options.ApiBaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// Builds the full chat/completions URI from the configured base URL,
    /// defaulting to the local Ollama endpoint when the base URL is not set.
    /// </summary>
    private static Uri BuildChatCompletionsUri(string apiBaseUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "http://localhost:11434/v1"
            : apiBaseUrl.Trim().TrimEnd('/');

        return new Uri($"{baseUrl}/chat/completions");
    }
}
