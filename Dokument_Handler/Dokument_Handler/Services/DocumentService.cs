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
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".com", ".dll", ".exe", ".hta", ".htm", ".html",
        ".js", ".mjs", ".ps1", ".sh", ".svg"
    };
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

        if (string.Equals(_storageRoot, effectiveRoot, PathComparison)
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
        _store.Categories = _store.Categories
            .Where(IsValidCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!_store.Categories.Contains("Allgemein", StringComparer.OrdinalIgnoreCase))
            _store.Categories.Insert(0, "Allgemein");

        foreach (var cat in _store.Categories)
            Directory.CreateDirectory(ResolveStoragePath(Sanitize(cat)));
    }

    private void LoadMetadata()
    {
        _store = new DocumentStore();

        if (!File.Exists(_metaFile))
            return;

        try
        {
            var json = File.ReadAllText(_metaFile);
            _store = JsonSerializer.Deserialize<DocumentStore>(json) ?? new DocumentStore();
        }
        catch (JsonException)
        {
            var backupFile = _metaFile + ".bak";
            if (!File.Exists(backupFile)) throw;

            var json = File.ReadAllText(backupFile);
            _store = JsonSerializer.Deserialize<DocumentStore>(json) ?? new DocumentStore();
        }

        _store.Documents ??= [];
        _store.Categories ??= [];
        foreach (var document in _store.Documents)
        {
            document.FileName ??= string.Empty;
            document.OriginalFileName ??= string.Empty;
            document.Category ??= "Allgemein";
            document.Tags ??= [];
            document.Description ??= string.Empty;
            document.ContentType ??= "application/octet-stream";
            document.RelativePath ??= string.Empty;
        }
    }

    private void SaveMetadata()
    {
        var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = true });
        WriteAtomically(_metaFile, json);
    }

    private static void WriteAtomically(string path, string content)
    {
        var tempFile = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempFile, content);
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tempFile, path, true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>Returns a read-only snapshot of all stored document entries.</summary>
    public IReadOnlyList<DocumentEntry> GetAll()
    {
        lock (_sync)
        {
            EnsureStoreLoaded();
            return _store.Documents.Select(CloneEntry).ToList().AsReadOnly();
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
            var entry = _store.Documents.FirstOrDefault(d => d.Id == id);
            return entry == null ? null : CloneEntry(entry);
        }
    }

    /// <summary>
    /// Stores the uploaded file stream on disk and creates a new <see cref="DocumentEntry"/>.
    /// The file is written outside the metadata lock to avoid blocking other readers.
    /// </summary>
    public async Task<DocumentEntry> UploadAsync(Stream fileStream, string originalFileName,
        string category, List<string> tags, string description, string contentType)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        originalFileName = Path.GetFileName(originalFileName.Trim());

        string fullPath;
        string storageRootAtStart;
        DocumentEntry entry;

        lock (_sync)
        {
            EnsureStoreLoaded();
            storageRootAtStart = _storageRoot;

            var safeCategory = _store.Categories.Contains(category) ? category : "Allgemein";
            var categoryDir = Path.Combine(_storageRoot, Sanitize(safeCategory));
            Directory.CreateDirectory(categoryDir);

            var ext = GetSafeExtension(originalFileName);
            if (BlockedExtensions.Contains(ext))
                throw new InvalidOperationException("Dieser Dateityp ist aus Sicherheitsgründen nicht zulässig.");
            var storedName = $"{Guid.NewGuid()}{ext}";
            fullPath = ResolveStoragePath(Path.Combine(Sanitize(safeCategory), storedName));

            entry = new DocumentEntry
            {
                FileName = storedName,
                OriginalFileName = originalFileName,
                Category = safeCategory,
                Tags = tags.ToList(),
                Description = description,
                ContentType = contentType,
                RelativePath = Path.Combine(Sanitize(safeCategory), storedName)
            };

        }

        var tempPath = fullPath + $".{Guid.NewGuid():N}.upload";
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await fileStream.CopyToAsync(fs);
                await fs.FlushAsync();
            }

            var uploadedLength = new FileInfo(tempPath).Length;
            lock (_sync)
            {
                EnsureStoreLoaded();
                if (!string.Equals(storageRootAtStart, _storageRoot, PathComparison))
                    throw new InvalidOperationException("Der Speicherpfad wurde während des Uploads geändert. Bitte erneut versuchen.");
                var quota = _settingsService.GetStorageOptions().MaxTotalSizeBytes;
                var currentSize = _store.Documents.Sum(document => Math.Max(0, document.FileSizeBytes));
                if (quota > 0 && uploadedLength > quota - currentSize)
                    throw new InvalidOperationException("Das konfigurierte Speicherlimit ist erreicht.");

                File.Move(tempPath, fullPath);
                entry.FileSizeBytes = uploadedLength;
                _store.Documents.Add(entry);
                try
                {
                    SaveMetadata();
                }
                catch
                {
                    _store.Documents.Remove(entry);
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                    throw;
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        return CloneEntry(entry);
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

            var targetCategory = _store.Categories.Contains(updated.Category) ? updated.Category : "Allgemein";
            var oldCategoryDir = ResolveStoragePath(Sanitize(existing.Category));
            var newCategoryDir = ResolveStoragePath(Sanitize(targetCategory));
            Directory.CreateDirectory(newCategoryDir);

            if (!string.Equals(existing.Category, targetCategory, StringComparison.OrdinalIgnoreCase))
            {
                // Move the physical file when the category (and therefore directory) changes.
                var oldPath = Path.Combine(oldCategoryDir, existing.FileName);
                var newPath = Path.Combine(newCategoryDir, existing.FileName);
                if (File.Exists(oldPath)) File.Move(oldPath, newPath);
                existing.RelativePath = Path.Combine(Sanitize(targetCategory), existing.FileName);
            }

            existing.Category = targetCategory;
            existing.Tags = updated.Tags.ToList();
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

            var fullPath = ResolveStoragePath(entry.RelativePath);
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
            return ResolveStoragePath(entry.RelativePath);
        }
    }

    /// <summary>
    /// Adds a new category and creates its directory on disk.
    /// Does nothing if the category already exists.
    /// </summary>
    public void AddCategory(string category)
    {
        category = category.Trim();
        ValidateCategory(category);

        lock (_sync)
        {
            EnsureStoreLoaded();

            if (!_store.Categories.Contains(category))
            {
                _store.Categories.Add(category);
                Directory.CreateDirectory(ResolveStoragePath(Sanitize(category)));
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

            if (string.IsNullOrWhiteSpace(query)) return _store.Documents.Select(CloneEntry).ToList();

            query = query.ToLowerInvariant();

            return _store.Documents
                .Select(d => (doc: d, score: FuzzyScore(d, query)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => CloneEntry(x.doc))
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

    private string ResolveStoragePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("Absolute Dokumentpfade sind nicht zulässig.");

        var root = Path.GetFullPath(_storageRoot);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootPrefix, PathComparison))
            throw new InvalidOperationException("Der Dokumentpfad liegt außerhalb des Speicherverzeichnisses.");

        return candidate;
    }

    private static void ValidateCategory(string category)
    {
        if (!IsValidCategory(category))
            throw new ArgumentException("Der Kategoriename ist ungültig.", nameof(category));
    }

    private static bool IsValidCategory(string category) =>
        !string.IsNullOrWhiteSpace(category)
        && category.Length <= 80
        && category is not "." and not ".."
        && !category.Any(char.IsControl)
        && category.IndexOfAny(['/', '\\']) < 0;

    private static string GetSafeExtension(string fileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        if (extension.Length > 20 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
            return string.Empty;
        return extension.ToLowerInvariant();
    }

    private static DocumentEntry CloneEntry(DocumentEntry source) => new()
    {
        Id = source.Id,
        FileName = source.FileName,
        OriginalFileName = source.OriginalFileName,
        Category = source.Category,
        Tags = source.Tags.ToList(),
        Description = source.Description,
        UploadedAt = source.UploadedAt,
        FileSizeBytes = source.FileSizeBytes,
        ContentType = source.ContentType,
        RelativePath = source.RelativePath
    };
}
