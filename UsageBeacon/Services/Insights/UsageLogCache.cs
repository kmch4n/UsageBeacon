using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBeacon.Models.Insights;

namespace UsageBeacon.Services.Insights;

/// <summary>
/// Incremental parse cache for the ~1 GB session log corpus. Each file's
/// extracted entries are stored with its length and write time so unchanged
/// files are never reparsed. Entries of deleted files are retained on
/// purpose: Claude Code prunes transcripts after roughly 30 days, so the
/// cache is the primary source for older days, not just an accelerator.
/// Only numeric usage values and model names are persisted — never content.
/// </summary>
public sealed class UsageLogCache
{
    private const int SchemaVersion = 1;

    private readonly string _cachePath;
    private readonly Dictionary<string, CachedFile> _files;

    private UsageLogCache(string cachePath, Dictionary<string, CachedFile> files)
    {
        _cachePath = cachePath;
        _files = files;
    }

    public static UsageLogCache Load(string cachePath)
    {
        try
        {
            if (File.Exists(cachePath))
            {
                var document = JsonSerializer.Deserialize<CacheDocument>(
                    File.ReadAllText(cachePath));
                if (document?.Files != null && document.SchemaVersion == SchemaVersion)
                {
                    // Re-wrap: deserialization loses the case-insensitive
                    // path comparer of the original dictionary.
                    return new UsageLogCache(cachePath, new Dictionary<string, CachedFile>(
                        document.Files, StringComparer.OrdinalIgnoreCase));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt cache is rebuilt from the logs on the next scan.
        }
        return new UsageLogCache(cachePath, new Dictionary<string, CachedFile>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the entries for one log file, reparsing only when the file is
    /// new or its size or write time changed since the cached parse.
    /// </summary>
    public IReadOnlyList<TokenUsageEntry> GetEntries(
        string path,
        long length,
        DateTime lastWriteUtc,
        Func<string, IReadOnlyList<TokenUsageEntry>> parser)
    {
        if (_files.TryGetValue(path, out var cached) &&
            cached.Length == length &&
            cached.LastWriteUtc == lastWriteUtc)
            return cached.Entries;

        var entries = parser(path);
        _files[path] = new CachedFile(length, lastWriteUtc, entries.ToList());
        return entries;
    }

    /// <summary>All cached entries, including those of files deleted since.</summary>
    public IEnumerable<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>> AllEntries()
    {
        foreach (var (path, file) in _files)
            yield return new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>(path, file.Entries);
    }

    /// <summary>Persists the cache; failures are non-fatal by design.</summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new CacheDocument(SchemaVersion, _files));
            // Write-then-move keeps the previous cache intact if this write is
            // interrupted; the cache is the only history for pruned transcripts.
            var tempPath = _cachePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The dashboard still works; the next scan just reparses more files.
        }
    }

    private sealed record CachedFile(
        [property: JsonPropertyName("len")] long Length,
        [property: JsonPropertyName("mtime")] DateTime LastWriteUtc,
        [property: JsonPropertyName("entries")] List<TokenUsageEntry> Entries);

    private sealed record CacheDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("files")] Dictionary<string, CachedFile> Files);
}
