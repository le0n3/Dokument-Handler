namespace Dokument_Handler.Models;

public class DocumentEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string Category { get; set; } = "Allgemein";
    public List<string> Tags { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.Now;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
}

public class DocumentStore
{
    public List<DocumentEntry> Documents { get; set; } = new();
    public List<string> Categories { get; set; } = new()
    {
        "Allgemein", "E-Mail", "Rechnungen", "Verträge", "Behörden", "Versicherung",
        "Steuern", "Medizin", "Arbeit", "Wohnen", "Sonstiges"
    };
}
