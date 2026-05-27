using System.Text;
using Dokument_Handler.Services;

namespace Dokument_Handler.Tests;

public class DocumentServiceTests
{
    [Fact]
    public async Task UploadAsync_StoresFileAndMetadata()
    {
        using var workspace = new TestWorkspace();
        var sut = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test-content"));

        var entry = await sut.UploadAsync(
            stream,
            "scan.pdf",
            "Unknown",
            ["rechnung"],
            "Testdokument",
            "application/pdf");

        Assert.Equal("Allgemein", entry.Category);
        Assert.True(File.Exists(sut.GetFullPath(entry)));

        var byId = sut.GetById(entry.Id);
        Assert.NotNull(byId);
        Assert.Equal("scan.pdf", byId.OriginalFileName);
        Assert.True(byId.FileSizeBytes > 0);
    }

    [Fact]
    public async Task UpdateEntry_MovesFileToNewCategory()
    {
        using var workspace = new TestWorkspace();
        var sut = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("move-me"));

        var entry = await sut.UploadAsync(stream, "doc.pdf", "Allgemein", [], "", "application/pdf");
        var oldPath = sut.GetFullPath(entry);

        sut.AddCategory("Rechnungen");
        var update = new Dokument_Handler.Models.DocumentEntry
        {
            Id = entry.Id,
            FileName = entry.FileName,
            OriginalFileName = entry.OriginalFileName,
            Category = "Rechnungen",
            Tags = ["wichtig"],
            Description = "updated",
            ContentType = entry.ContentType,
            RelativePath = entry.RelativePath,
            UploadedAt = entry.UploadedAt,
            FileSizeBytes = entry.FileSizeBytes
        };
        sut.UpdateEntry(update);

        var updated = sut.GetById(entry.Id);
        Assert.NotNull(updated);
        Assert.Equal("Rechnungen", updated.Category);
        Assert.Equal("updated", updated.Description);
        Assert.Contains("wichtig", updated.Tags);

        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(sut.GetFullPath(updated)));
    }

    [Fact]
    public async Task DeleteEntry_RemovesFileAndEntry()
    {
        using var workspace = new TestWorkspace();
        var sut = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("delete-me"));

        var entry = await sut.UploadAsync(stream, "doc.txt", "Allgemein", [], "", "text/plain");
        var filePath = sut.GetFullPath(entry);

        sut.DeleteEntry(entry.Id);

        Assert.Null(sut.GetById(entry.Id));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task FuzzySearch_ReturnsMatchingDocument()
    {
        using var workspace = new TestWorkspace();
        var sut = new DocumentService(workspace.Environment, workspace.AppSettingsService);

        await using var streamOne = new MemoryStream(Encoding.UTF8.GetBytes("one"));
        await using var streamTwo = new MemoryStream(Encoding.UTF8.GetBytes("two"));

        var matching = await sut.UploadAsync(streamOne, "rechnung_mai.pdf", "Rechnungen", ["steuer"], "", "application/pdf");
        await sut.UploadAsync(streamTwo, "urlaub.jpg", "Allgemein", ["foto"], "", "image/jpeg");

        var result = sut.FuzzySearch("rechnung");

        Assert.NotEmpty(result);
        Assert.Equal(matching.Id, result[0].Id);
    }

    [Fact]
    public async Task UploadEmailAttachmentAsync_SetsEmailCategory()
    {
        using var workspace = new TestWorkspace();
        var sut = new DocumentService(workspace.Environment, workspace.AppSettingsService);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("mail-attachment"));

        var entry = await sut.UploadEmailAttachmentAsync(stream, "mail.pdf", "application/pdf", "Von: test@example.com");

        Assert.Equal("E-Mail", entry.Category);
        Assert.Equal("Von: test@example.com", entry.Description);
        Assert.Contains("E-Mail", sut.GetCategories());
    }
}
