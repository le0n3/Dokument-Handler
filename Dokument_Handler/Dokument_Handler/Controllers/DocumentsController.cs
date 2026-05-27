using Dokument_Handler.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dokument_Handler.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _svc;

    public DocumentsController(DocumentService svc) => _svc = svc;

    [HttpGet("{id}/file")]
    public IActionResult GetFile(Guid id)
    {
        var entry = _svc.GetById(id);
        if (entry == null) return NotFound();

        var path = _svc.GetFullPath(entry);
        if (!System.IO.File.Exists(path)) return NotFound();

        var stream = System.IO.File.OpenRead(path);
        return File(stream, entry.ContentType, entry.OriginalFileName);
    }

    [HttpGet("{id}/inline")]
    public IActionResult GetInline(Guid id)
    {
        var entry = _svc.GetById(id);
        if (entry == null) return NotFound();

        var path = _svc.GetFullPath(entry);
        if (!System.IO.File.Exists(path)) return NotFound();

        var stream = System.IO.File.OpenRead(path);
        Response.Headers.Append("Content-Disposition", $"inline; filename=\"{entry.OriginalFileName}\"");
        return File(stream, entry.ContentType);
    }

    [HttpPost("email")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> UploadEmailAttachments(
        [FromForm] List<IFormFile> attachments,
        [FromForm] string? subject,
        [FromForm] string? from)
    {
        if (attachments == null || attachments.Count == 0)
            return BadRequest("Mindestens ein Anhang ist erforderlich.");

        var saved = new List<object>();
        var description = string.Join(" | ", new[]
        {
            string.IsNullOrWhiteSpace(subject) ? null : $"Betreff: {subject}",
            string.IsNullOrWhiteSpace(from) ? null : $"Von: {from}"
        }.Where(x => x is not null));

        foreach (var file in attachments.Where(a => a.Length > 0))
        {
            await using var stream = file.OpenReadStream();
            var entry = await _svc.UploadEmailAttachmentAsync(
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
}
