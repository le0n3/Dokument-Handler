using System.Text.Json;
using Dokument_Handler.Models;

namespace Dokument_Handler.Services;

/// <summary>
/// Handles all document storage operations: upload, retrieval, update, deletion, and search.
/// Metadata is persisted as JSON in <c>_metadata.json</c> inside the configured storage root.
/// All public methods are thread-safe.
/// </summary>
public class DocumentService
{
    private const string EmailCategory = "E-Mail";
    private readonly IWebHostEnvironment _env;
    private readonly AppSettingsService _settingsService;

    // Lock object used to serialise all access to _store and the file system.
    private readonly object _sync = new();

    private string _storageRoot = string.Empty;
    private string _metaFile = string.Empty;
    private DocumentStore _store = new();

    public DocumentService(IWebHostEnvironment env, AppSettingsService settingsService)
    {
        _env = env;
        _settingsService = settingsService;
        EnsureStoreLoaded();
    }

    /// <summary>
    /// Ensures the store is initialised for the currently configured storage root.
    /// Reloads from disk when the root path has changed or the metadata file is missing.
    /// </summary>
    private void EnsureStoreLoaded()
    {
        var configured = _settingsService.GetStorageOptions().RootPath?.Trim() ?? string.Empty;
        var effectiveRoot = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_env.ContentRootPath, "DocumentStorage")
            : configured;

        if (string.Equals(_storageRoot, effectiveRoot, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_metaFile)
            && File.Exists(_metaFile))
        {
            return;
        }

        _storageRoot = effectiveRoot;
        _metaFile = Path.Combine(_storageRoot, "_metadata.json");

        Directory.CreateDirectory(_storageRoot);
        LoadMetadata();
        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_storageRoot);
        foreach (var cat in _store.Categories)
            Directory.CreateDirectory(Path.Combine(_storageRoot, Sanitize(cat)));
    }

    private void LoadMetadata()
    {
        _store = new DocumentStore();

        if (!File.Exists(_metaFile))
            return;

        var json = File.ReadAllText(_metaFile);
        _store = JsonSerializer.Deserialize<DocumentStore>(json) ?? new DocumentStore();
    }

    private void SaveMetadata()
    {
        var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_metaFile, json);
    }

    /// <summary>Returns a read-only snapshot of all stored document entries.</summary>
    public IReadOnlyList<DocumentEntry> GetAll()
    {
        lock (_sync)
        {
            EnsureStoreLoaded();
            return _store.Documents.ToList().AsReadOnly();
        }
    }

    /// <summary>Returns a read-only list of all available category names.</summary>
    public IReadOnlyList<string> GetCategories()
    {
        lock (_sync)
        {
            EnsureStoreLoaded();
            return _store.Categories.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Returns the document entry with the given <paramref name="id"/>,
    /// or <see langword="null"/> if no matching entry exists.
    /// </summary>
    public DocumentEntry? GetById(Guid id)
    {
        lock (_sync)
        {
            EnsureStoreLoaded();
            return _store.Documents.FirstOrDefault(d => d.Id == id);
        }
    }

    /// <summary>
    /// Stores the uploaded file stream on disk and creates a new <see cref="DocumentEntry"/>.
    /// The file is written outside the metadata lock to avoid blocking other readers.
    /// </summary>
    public async Task<DocumentEntry> UploadAsync(Stream fileStream, string originalFileName,
        string category, List<string> tags, string description, string contentType)
    {
        string fullPath;
        DocumentEntry entry;

        lock (_sync)
        {
            EnsureStoreLoaded();

            var safeCategory = _store.Categories.Contains(category) ? category : "Allgemein";
            var categoryDir = Path.Combine(_storageRoot, Sanitize(safeCategory));
            Directory.CreateDirectory(categoryDir);

            var ext = Path.GetExtension(originalFileName);
            var storedName = $"{Guid.NewGuid()}{ext}";
            fullPath = Path.Combine(categoryDir, storedName);

            entry = new DocumentEntry
            {
                FileName = storedName,
                OriginalFileName = originalFileName,
                Category = safeCategory,
                Tags = tags,
                Description = description,
                ContentType = contentType,
                RelativePath = Path.Combine(Sanitize(safeCategory), storedName)
            };

            _store.Documents.Add(entry);
        }

        await using (var fs = File.Create(fullPath))
        {
            await fileStream.CopyToAsync(fs);
        }

        lock (_sync)
        {
            entry.FileSizeBytes = new FileInfo(fullPath).Length;
            SaveMetadata();
        }

        return entry;
    }

    /// <summary>
    /// Uploads a file stream as an email attachment, placing it in the "E-Mail" category.
    /// The category is created automatically if it does not exist yet.
    /// </summary>
    public async Task<DocumentEntry> UploadEmailAttachmentAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        string? emailDescription = null)
    {
        if (!GetCategories().Contains(EmailCategory))
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

    /// <summary>
    /// Updates the metadata of an existing entry and moves the file to the new category
    /// directory when the category has changed.
    /// </summary>
    public void UpdateEntry(DocumentEntry updated)
    {
        lock (_sync)
        {
            EnsureStoreLoaded();

            var existing = _store.Documents.FirstOrDefault(d => d.Id == updated.Id);
            if (existing == null) return;

            var oldCategoryDir = Path.Combine(_storageRoot, Sanitize(existing.Category));
            var newCategoryDir = Path.Combine(_storageRoot, Sanitize(updated.Category));
            Directory.CreateDirectory(newCategoryDir);

            if (!string.Equals(existing.Category, updated.Category, StringComparison.OrdinalIgnoreCase))
            {
                // Move the physical file when the category (and therefore directory) changes.
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
    }

    /// <summary>Removes a document entry and deletes the corresponding file from disk.</summary>
    public void DeleteEntry(Guid id)
    {
        lock (_sync)
        {
            EnsureStoreLoaded();

            var entry = _store.Documents.FirstOrDefault(d => d.Id == id);
            if (entry == null) return;

            var fullPath = Path.Combine(_storageRoot, entry.RelativePath);
            if (File.Exists(fullPath)) File.Delete(fullPath);

            _store.Documents.Remove(entry);
            SaveMetadata();
        }
    }

    /// <summary>Returns the absolute file system path for a given document entry.</summary>
    public string GetFullPath(DocumentEntry entry)
    {
        lock (_sync)
        {
            EnsureStoreLoaded();
            return Path.Combine(_storageRoot, entry.RelativePath);
        }
    }

    /// <summary>
    /// Adds a new category and creates its directory on disk.
    /// Does nothing if the category already exists.
    /// </summary>
    public void AddCategory(string category)
    {
        lock (_sync)
        {
            EnsureStoreLoaded();

            if (!_store.Categories.Contains(category))
            {
                _store.Categories.Add(category);
                Directory.CreateDirectory(Path.Combine(_storageRoot, Sanitize(category)));
                SaveMetadata();
            }
        }
    }

    /// <summary>
    /// Performs a fuzzy search across file name, description, tags, and category.
    /// Returns all documents when <paramref name="query"/> is empty, otherwise returns
    /// results ordered by relevance score (highest first).
    /// </summary>
    public List<DocumentEntry> FuzzySearch(string query)
    {
        lock (_sync)
        {
            EnsureStoreLoaded();

            if (string.IsNullOrWhiteSpace(query)) return _store.Documents.ToList();

            query = query.ToLowerInvariant();

            return _store.Documents
                .Select(d => (doc: d, score: FuzzyScore(d, query)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.doc)
                .ToList();
        }
    }

    /// <summary>
    /// Calculates a relevance score for <paramref name="doc"/> against the lowercase <paramref name="query"/>.
    /// Higher-priority fields (file name, tags) contribute more to the score than lower-priority ones.
    /// </summary>
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

    /// <summary>
    /// Returns <see langword="true"/> when all characters of <paramref name="needle"/>
    /// appear in order within <paramref name="haystack"/>.
    /// </summary>
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

    /// <summary>Replaces characters that are invalid in file/directory names with underscores.</summary>
    private static string Sanitize(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
