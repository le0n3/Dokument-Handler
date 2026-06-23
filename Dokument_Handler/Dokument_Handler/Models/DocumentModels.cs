namespace Dokument_Handler.Models;

/// <summary>
/// Represents a single stored document with its metadata.
/// </summary>
public class DocumentEntry
{
    /// <summary>Gets or sets the unique identifier for this document.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the internal file name used on disk (includes a GUID prefix).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the original file name as supplied by the user or email client.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the category this document belongs to.</summary>
    public string Category { get; set; } = "Allgemein";

    /// <summary>Gets or sets the list of user-defined tags for this document.</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Gets or sets an optional free-text description or note.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the date and time when the document was uploaded.</summary>
    public DateTime UploadedAt { get; set; } = DateTime.Now;

    /// <summary>Gets or sets the file size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Gets or sets the MIME content type (e.g. <c>application/pdf</c>).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Gets or sets the path relative to the storage root directory.</summary>
    public string RelativePath { get; set; } = string.Empty;
}

/// <summary>
/// Root data structure persisted in <c>_metadata.json</c>.
/// Contains all document entries and the list of available categories.
/// </summary>
public class DocumentStore
{
    /// <summary>Gets or sets all stored document entries.</summary>
    public List<DocumentEntry> Documents { get; set; } = new();

    /// <summary>Gets or sets the available document categories.</summary>
    public List<string> Categories { get; set; } = new()
    {
        "Allgemein", "E-Mail", "Rechnungen", "Verträge", "Behörden", "Versicherung",
        "Steuern", "Medizin", "Arbeit", "Wohnen", "Sonstiges"
    };
}
