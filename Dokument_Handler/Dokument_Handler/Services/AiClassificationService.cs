using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dokument_Handler.Models;
using UglyToad.PdfPig;

namespace Dokument_Handler.Services;

public class AiClassificationOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "llama3.2";
    public int MaxTextChars { get; set; } = 4000;
    public bool UseJsonFormat { get; set; } = false;
}

public class AiClassificationResult
{
    public string Category { get; set; } = "Allgemein";
    public List<string> Tags { get; set; } = new();
    public string Description { get; set; } = string.Empty;
}

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
    /// Extrahiert Text aus einem PDF und lässt ihn vom LLM klassifizieren.
    /// Gibt null zurück wenn das Feature deaktiviert ist oder ein Fehler auftritt.
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

    private string ExtractPdfText(string filePath, int maxTextChars)
    {
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

    private static Uri BuildChatCompletionsUri(string apiBaseUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "http://localhost:11434/v1"
            : apiBaseUrl.Trim().TrimEnd('/');

        return new Uri($"{baseUrl}/chat/completions");
    }
}
