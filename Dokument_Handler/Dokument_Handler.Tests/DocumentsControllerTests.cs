using System.Text;
using Dokument_Handler.Controllers;
using Dokument_Handler.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Dokument_Handler.Tests;

public class DocumentsControllerTests
{
    [Fact]
    public async Task UploadEmailAttachments_WhenEmpty_ReturnsBadRequest()
    {
        using var workspace = new TestWorkspace();
        var docService = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        var sut = new DocumentsController(docService);

        var result = await sut.UploadEmailAttachments([], null, null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Mindestens ein Anhang ist erforderlich.", badRequest.Value);
    }

    [Fact]
    public async Task UploadEmailAttachments_SavesAttachmentsAndReturnsOk()
    {
        using var workspace = new TestWorkspace();
        var docService = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        var sut = new DocumentsController(docService);

        var bytes = Encoding.UTF8.GetBytes("file-content");
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "attachments", "invoice.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var result = await sut.UploadEmailAttachments([formFile], "Monat", "test@example.com");

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(docService.GetAll());
        Assert.Equal("E-Mail", docService.GetAll()[0].Category);
    }

    [Fact]
    public async Task GetFile_WhenDocumentExists_ReturnsFileResult()
    {
        using var workspace = new TestWorkspace();
        var docService = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        var controller = new DocumentsController(docService);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("file"));
        var entry = await docService.UploadAsync(stream, "doc.txt", "Allgemein", [], "", "text/plain");

        var result = controller.GetFile(entry.Id);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("text/plain", fileResult.ContentType);
        Assert.Equal("doc.txt", fileResult.FileDownloadName);
    }

    [Fact]
    public void GetFile_WhenDocumentMissing_ReturnsNotFound()
    {
        using var workspace = new TestWorkspace();
        var docService = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        var controller = new DocumentsController(docService);

        var result = controller.GetFile(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }
}
