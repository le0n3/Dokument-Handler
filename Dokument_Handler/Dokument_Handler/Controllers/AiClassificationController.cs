using Dokument_Handler.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dokument_Handler.Controllers;

[ApiController]
[Route("api/ai")]
[EnableRateLimiting("expensive")]
public class AiClassificationController : ControllerBase
{
    private readonly DocumentService _documentService;
    private readonly AiClassificationService _aiService;

    public AiClassificationController(DocumentService documentService, AiClassificationService aiService)
    {
        _documentService = documentService;
        _aiService = aiService;
    }

    /// <summary>
    /// Classifies a single document by its ID using the configured LLM.
    /// </summary>
    [HttpPost("classify/{id:guid}")]
    public async Task<IActionResult> ClassifyOne(Guid id)
    {
        if (!_aiService.IsEnabled)
            return BadRequest("AI-Klassifizierung ist nicht aktiviert. Bitte ApiKey in appsettings.json setzen.");

        var entry = _documentService.GetById(id);
        if (entry == null) return NotFound();

        var fullPath = _documentService.GetFullPath(entry);
        if (!System.IO.File.Exists(fullPath)) return NotFound("Datei nicht gefunden.");

        if (!entry.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            && !entry.OriginalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Nur PDF-Dateien werden unterstützt.");

        var result = await _aiService.ClassifyDocumentAsync(fullPath, _documentService.GetCategories());
        if (result == null)
            return StatusCode(500, "AI-Klassifizierung hat kein Ergebnis zurückgegeben.");

        entry.Category = result.Category;
        entry.Tags = result.Tags;
        if (!string.IsNullOrWhiteSpace(result.Description))
            entry.Description = result.Description;

        _documentService.UpdateEntry(entry);

        return Ok(new
        {
            entry.Id,
            entry.OriginalFileName,
            entry.Category,
            entry.Tags,
            entry.Description
        });
    }

    /// <summary>
    /// Classifies all PDF documents in the system in a single batch.
    /// Returns a progress report with the status of each document.
    /// </summary>
    [HttpPost("classify-all")]
    public async Task<IActionResult> ClassifyAll()
    {
        if (!_aiService.IsEnabled)
            return BadRequest("AI-Klassifizierung ist nicht aktiviert. Bitte ApiKey in appsettings.json setzen.");

        var allDocs = _documentService.GetAll()
            .Where(d => d.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                        || d.OriginalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var results = new List<object>();
        var categories = _documentService.GetCategories();

        foreach (var entry in allDocs)
        {
            var fullPath = _documentService.GetFullPath(entry);
            if (!System.IO.File.Exists(fullPath))
            {
                results.Add(new { entry.Id, entry.OriginalFileName, Status = "Datei fehlt" });
                continue;
            }

            try
            {
                var result = await _aiService.ClassifyDocumentAsync(fullPath, categories);
                if (result == null)
                {
                    results.Add(new { entry.Id, entry.OriginalFileName, Status = "Kein Text extrahiert" });
                    continue;
                }

                entry.Category = result.Category;
                entry.Tags = result.Tags;
                if (!string.IsNullOrWhiteSpace(result.Description))
                    entry.Description = result.Description;

                _documentService.UpdateEntry(entry);

                results.Add(new
                {
                    entry.Id,
                    entry.OriginalFileName,
                    Status = "Klassifiziert",
                    result.Category,
                    result.Tags,
                    result.Description
                });
            }
            catch (Exception ex)
            {
                results.Add(new { entry.Id, entry.OriginalFileName, Status = $"Fehler: {ex.Message}" });
            }
        }

        return Ok(new
        {
            Total = allDocs.Count,
            Processed = results.Count,
            Results = results
        });
    }

    /// <summary>
    /// Returns whether the AI classification service is currently enabled and configured.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new { Enabled = _aiService.IsEnabled });
    }
}
