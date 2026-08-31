using Dokument_Handler.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dokument_Handler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;

    public DocumentsController(DocumentService documentService) => _documentService = documentService;

    /// <summary>
    /// Returns the file for the given document ID as a download attachment.
    /// </summary>
    [HttpGet("{id}/file")]
    public IActionResult GetFile(Guid id)
    {
        var entry = _documentService.GetById(id);
        if (entry == null) return NotFound();

        var path = _documentService.GetFullPath(entry);
        if (!System.IO.File.Exists(path)) return NotFound();

        var stream = System.IO.File.OpenRead(path);
        return File(stream, entry.ContentType, entry.OriginalFileName);
    }

    /// <summary>
    /// Returns the file for the given document ID as an inline view (e.g. for PDF/image preview).
    /// </summary>
    [HttpGet("{id}/inline")]
    public IActionResult GetInline(Guid id)
    {
        var entry = _documentService.GetById(id);
        if (entry == null) return NotFound();

        var path = _documentService.GetFullPath(entry);
        if (!System.IO.File.Exists(path)) return NotFound();

        if (!TryGetSafeInlineContentType(entry, out var contentType))
            return BadRequest("Dieser Dateityp kann aus Sicherheitsgründen nur heruntergeladen werden.");

        Response.Headers.XContentTypeOptions = "nosniff";
        var stream = System.IO.File.OpenRead(path);
        return File(stream, contentType);
    }

    /// <summary>
    /// Accepts one or more email attachments sent via an email client integration
    /// and stores them as documents in the "E-Mail" category.
    /// </summary>
    [HttpPost("email")]
    [EnableRateLimiting("expensive")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> UploadEmailAttachments(
        [FromForm] List<IFormFile> attachments,
        [FromForm] string? subject,
        [FromForm] string? from)
    {
        if (attachments == null || attachments.Count == 0)
            return BadRequest("Mindestens ein Anhang ist erforderlich.");

        var saved = new List<object>();

        // Build a combined description from the email subject and sender.
        var description = string.Join(" | ", new[]
        {
            string.IsNullOrWhiteSpace(subject) ? null : $"Betreff: {subject}",
            string.IsNullOrWhiteSpace(from) ? null : $"Von: {from}"
        }.Where(x => x is not null));

        foreach (var file in attachments.Where(a => a.Length > 0))
        {
            await using var stream = file.OpenReadStream();
            var entry = await _documentService.UploadEmailAttachmentAsync(
                stream,
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                description);

            saved.Add(new
            {
                entry.Id,
                entry.OriginalFileName,
                entry.Category,
                entry.Tags,
                entry.UploadedAt
            });
        }

        if (saved.Count == 0)
            return BadRequest("Keine gültigen Anhänge gefunden.");

        return Ok(saved);
    }

    private static bool TryGetSafeInlineContentType(Dokument_Handler.Models.DocumentEntry entry, out string contentType)
    {
        var extension = Path.GetExtension(entry.OriginalFileName).ToLowerInvariant();
        contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => string.Empty
        };
        return contentType.Length > 0;
    }
}
