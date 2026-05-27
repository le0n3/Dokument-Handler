using System.Text.Json;
using Dokument_Handler.Models;

namespace Dokument_Handler.Services;

public class DocumentService
{
    private const string EmailCategory = "E-Mail";
    private readonly IWebHostEnvironment _env;
    private readonly string _storageRoot;
    private readonly string _metaFile;
    private DocumentStore _store = new();

    public DocumentService(IWebHostEnvironment env)
    {
        _env = env;
        _storageRoot = Path.Combine(_env.ContentRootPath, "DocumentStorage");
        _metaFile = Path.Combine(_storageRoot, "_metadata.json");
        EnsureDirectories();
        LoadMetadata();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_storageRoot);
        foreach (var cat in _store.Categories)
            Directory.CreateDirectory(Path.Combine(_storageRoot, Sanitize(cat)));
    }

    private void LoadMetadata()
    {
        if (File.Exists(_metaFile))
        {
            var json = File.ReadAllText(_metaFile);
            _store = JsonSerializer.Deserialize<DocumentStore>(json) ?? new DocumentStore();
        }
        // ensure category folders exist
        foreach (var cat in _store.Categories)
            Directory.CreateDirectory(Path.Combine(_storageRoot, Sanitize(cat)));
    }

    private void SaveMetadata()
    {
        var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_metaFile, json);
    }

    public IReadOnlyList<DocumentEntry> GetAll() => _store.Documents.AsReadOnly();

    public IReadOnlyList<string> GetCategories() => _store.Categories.AsReadOnly();

    public DocumentEntry? GetById(Guid id) =>
        _store.Documents.FirstOrDefault(d => d.Id == id);

    public async Task<DocumentEntry> UploadAsync(Stream fileStream, string originalFileName,
        string category, List<string> tags, string description, string contentType)
    {
        var safeCategory = _store.Categories.Contains(category) ? category : "Allgemein";
        var categoryDir = Path.Combine(_storageRoot, Sanitize(safeCategory));
        Directory.CreateDirectory(categoryDir);

        var ext = Path.GetExtension(originalFileName);
        var storedName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(categoryDir, storedName);

        await using var fs = File.Create(fullPath);
        await fileStream.CopyToAsync(fs);

        var entry = new DocumentEntry
        {
            FileName = storedName,
            OriginalFileName = originalFileName,
            Category = safeCategory,
            Tags = tags,
            Description = description,
            FileSizeBytes = new FileInfo(fullPath).Length,
            ContentType = contentType,
            RelativePath = Path.Combine(Sanitize(safeCategory), storedName)
        };

        _store.Documents.Add(entry);
        SaveMetadata();
        return entry;
    }

    public async Task<DocumentEntry> UploadEmailAttachmentAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        string? emailDescription = null)
    {
        if (!_store.Categories.Contains(EmailCategory))
        {
            AddCategory(EmailCategory);
        }

        return await UploadAsync(
            fileStream,
            originalFileName,
            EmailCategory,
            new List<string>(),
            emailDescription ?? string.Empty,
            contentType);
    }

    public void UpdateEntry(DocumentEntry updated)
    {
        var existing = _store.Documents.FirstOrDefault(d => d.Id == updated.Id);
        if (existing == null) return;

        var oldCategoryDir = Path.Combine(_storageRoot, Sanitize(existing.Category));
        var newCategoryDir = Path.Combine(_storageRoot, Sanitize(updated.Category));
        Directory.CreateDirectory(newCategoryDir);

        if (!string.Equals(existing.Category, updated.Category, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(oldCategoryDir, existing.FileName);
            var newPath = Path.Combine(newCategoryDir, existing.FileName);
            if (File.Exists(oldPath)) File.Move(oldPath, newPath);
            existing.RelativePath = Path.Combine(Sanitize(updated.Category), existing.FileName);
        }

        existing.Category = updated.Category;
        existing.Tags = updated.Tags;
        existing.Description = updated.Description;
        existing.OriginalFileName = updated.OriginalFileName;
        SaveMetadata();
    }

    public void DeleteEntry(Guid id)
    {
        var entry = _store.Documents.FirstOrDefault(d => d.Id == id);
        if (entry == null) return;

        var fullPath = Path.Combine(_storageRoot, entry.RelativePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);

        _store.Documents.Remove(entry);
        SaveMetadata();
    }

    public string GetFullPath(DocumentEntry entry) =>
        Path.Combine(_storageRoot, entry.RelativePath);

    public void AddCategory(string category)
    {
        if (!_store.Categories.Contains(category))
        {
            _store.Categories.Add(category);
            Directory.CreateDirectory(Path.Combine(_storageRoot, Sanitize(category)));
            SaveMetadata();
        }
    }

    public List<DocumentEntry> FuzzySearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _store.Documents.ToList();

        query = query.ToLowerInvariant();

        return _store.Documents
            .Select(d => (doc: d, score: FuzzyScore(d, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.doc)
            .ToList();
    }

    private static int FuzzyScore(DocumentEntry doc, string query)
    {
        int score = 0;
        var name = doc.OriginalFileName.ToLowerInvariant();
        var desc = doc.Description.ToLowerInvariant();
        var cat = doc.Category.ToLowerInvariant();
        var tags = string.Join(" ", doc.Tags).ToLowerInvariant();

        // Exact substring matches get high score
        if (name.Contains(query)) score += 100;
        if (desc.Contains(query)) score += 60;
        if (cat.Contains(query)) score += 50;
        if (tags.Contains(query)) score += 80;

        // Token-based fuzzy matching
        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (name.Contains(token)) score += 30;
            if (desc.Contains(token)) score += 15;
            if (tags.Contains(token)) score += 25;
            if (cat.Contains(token)) score += 10;

            // Character-sequence matching (subsequence)
            if (IsSubsequence(token, name)) score += 5;
            if (IsSubsequence(token, tags)) score += 5;
        }

        return score;
    }

    private static bool IsSubsequence(string needle, string haystack)
    {
        int ni = 0;
        foreach (var c in haystack)
        {
            if (ni < needle.Length && c == needle[ni]) ni++;
            if (ni == needle.Length) return true;
        }
        return false;
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
