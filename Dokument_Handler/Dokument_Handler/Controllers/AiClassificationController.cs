using Dokument_Handler.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dokument_Handler.Controllers;

[ApiController]
[Route("api/ai")]
public class AiClassificationController : ControllerBase
{
    private readonly DocumentService _svc;
    private readonly AiClassificationService _ai;

    public AiClassificationController(DocumentService svc, AiClassificationService ai)
    {
        _svc = svc;
        _ai = ai;
    }

    /// <summary>
    /// Klassifiziert ein einzelnes Dokument per ID mit dem LLM.
    /// </summary>
    [HttpPost("classify/{id:guid}")]
    public async Task<IActionResult> ClassifyOne(Guid id)
    {
        if (!_ai.IsEnabled)
            return BadRequest("AI-Klassifizierung ist nicht aktiviert. Bitte ApiKey in appsettings.json setzen.");

        var entry = _svc.GetById(id);
        if (entry == null) return NotFound();

        var fullPath = _svc.GetFullPath(entry);
        if (!System.IO.File.Exists(fullPath)) return NotFound("Datei nicht gefunden.");

        if (!entry.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            && !entry.OriginalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Nur PDF-Dateien werden unterstützt.");

        var result = await _ai.ClassifyDocumentAsync(fullPath, _svc.GetCategories());
        if (result == null)
            return StatusCode(500, "AI-Klassifizierung hat kein Ergebnis zurückgegeben.");

        entry.Category = result.Category;
        entry.Tags = result.Tags;
        if (!string.IsNullOrWhiteSpace(result.Description))
            entry.Description = result.Description;

        _svc.UpdateEntry(entry);

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
    /// Klassifiziert ALLE PDFs im System per Batch.
    /// Gibt einen Fortschritts-Bericht zurück.
    /// </summary>
    [HttpPost("classify-all")]
    public async Task<IActionResult> ClassifyAll()
    {
        if (!_ai.IsEnabled)
            return BadRequest("AI-Klassifizierung ist nicht aktiviert. Bitte ApiKey in appsettings.json setzen.");

        var allDocs = _svc.GetAll()
            .Where(d => d.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                        || d.OriginalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var results = new List<object>();
        var categories = _svc.GetCategories();

        foreach (var entry in allDocs)
        {
            var fullPath = _svc.GetFullPath(entry);
            if (!System.IO.File.Exists(fullPath))
            {
                results.Add(new { entry.Id, entry.OriginalFileName, Status = "Datei fehlt" });
                continue;
            }

            try
            {
                var result = await _ai.ClassifyDocumentAsync(fullPath, categories);
                if (result == null)
                {
                    results.Add(new { entry.Id, entry.OriginalFileName, Status = "Kein Text extrahiert" });
                    continue;
                }

                entry.Category = result.Category;
                entry.Tags = result.Tags;
                if (!string.IsNullOrWhiteSpace(result.Description))
                    entry.Description = result.Description;

                _svc.UpdateEntry(entry);

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
    /// Gibt zurück ob der AI-Service aktiv ist.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new { Enabled = _ai.IsEnabled });
    }
}
