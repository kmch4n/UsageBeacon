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
/// Detailed entries older than <see cref="RetentionDays"/> are compacted into
/// exact token totals plus their identity hashes so lifetime usage remains
/// available without allowing the full entry payload to grow indefinitely.
/// Only numeric usage values and model names are persisted — never content.
/// </summary>
public sealed class UsageLogCache
{
    private const int SchemaVersion = 1;

    /// <summary>Days of parsed history the cache keeps before entries are dropped.</summary>
    public const int RetentionDays = 180;

    private readonly string _cachePath;
    private readonly Dictionary<string, CachedFile> _files;
    private readonly HashSet<long> _archivedEntryIds;
    private decimal _archivedInputTokens;
    private decimal _archivedOutputTokens;
    private DateTime? _archivedFirstUsageUtc;
    private bool _dirty;

    private UsageLogCache(
        string cachePath,
        Dictionary<string, CachedFile> files,
        ArchivedUsageDocument? archived)
    {
        _cachePath = cachePath;
        _files = files;
        _archivedEntryIds = new HashSet<long>(archived?.EntryIds ?? []);
        _archivedInputTokens = archived?.TotalInputTokens ?? 0m;
        _archivedOutputTokens = archived?.TotalOutputTokens ?? 0m;
        _archivedFirstUsageUtc = archived?.FirstUsageUtc;
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
                    return new UsageLogCache(
                        cachePath,
                        new Dictionary<string, CachedFile>(
                            document.Files,
                            StringComparer.OrdinalIgnoreCase),
                        document.Archived);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt cache is rebuilt from the logs on the next scan.
        }
        return new UsageLogCache(
            cachePath,
            new Dictionary<string, CachedFile>(StringComparer.OrdinalIgnoreCase),
            archived: null);
    }

    /// <summary>
    /// Returns the entries for one log file, reparsing only when the file is
    /// new or its size or write time changed since the cached parse.
    /// </summary>
    public IReadOnlyList<TokenUsageEntry> GetEntries(
        string path,
        long length,
        DateTime lastWriteUtc,
        Func<string, IReadOnlyList<TokenUsageEntry>> parser,
        int parserRevision = 0)
    {
        if (_files.TryGetValue(path, out var cached) &&
            cached.Length == length &&
            cached.LastWriteUtc == lastWriteUtc &&
            cached.ParserRevision == parserRevision)
            return cached.Entries;

        var entries = parser(path);
        _files[path] = new CachedFile(
            length,
            lastWriteUtc,
            entries.ToList(),
            parserRevision);
        _dirty = true;
        return entries;
    }

    /// <summary>
    /// Compacts entries older than the cutoff into lifetime token totals. Their
    /// identity hashes are retained so a later file reparse cannot count them
    /// twice. Per-file metadata remains unchanged to avoid unnecessary reparses.
    /// </summary>
    public void ArchiveBefore(DateTime cutoffUtc)
    {
        var seen = new HashSet<long>(_archivedEntryIds);
        foreach (var path in _files.Keys.OrderBy(
                     path => path,
                     StringComparer.OrdinalIgnoreCase).ToList())
        {
            var file = _files[path];
            var kept = new List<TokenUsageEntry>(file.Entries.Count);
            foreach (var entry in file.Entries)
            {
                // Keep the same deterministic first-path-wins rule used by
                // UsageAggregator, including across archived and detailed data.
                if (!seen.Add(entry.IdHash)) continue;

                if (entry.TimestampUtc >= cutoffUtc)
                {
                    kept.Add(entry);
                    continue;
                }

                _archivedEntryIds.Add(entry.IdHash);
                _archivedInputTokens += (decimal)entry.InputTokens +
                                        entry.CachedInputTokens +
                                        entry.CacheWrite5mTokens +
                                        entry.CacheWrite1hTokens;
                _archivedOutputTokens += entry.OutputTokens;
                if (_archivedFirstUsageUtc is null ||
                    entry.TimestampUtc < _archivedFirstUsageUtc)
                    _archivedFirstUsageUtc = entry.TimestampUtc;
            }
            if (kept.Count == file.Entries.Count) continue;
            _files[path] = file with { Entries = kept };
            _dirty = true;
        }
    }

    public ArchivedTokenUsage ArchivedUsage => new(
        _archivedInputTokens,
        _archivedOutputTokens,
        _archivedFirstUsageUtc);

    /// <summary>All cached entries, including those of files deleted since.</summary>
    public IEnumerable<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>> AllEntries()
    {
        foreach (var (path, file) in _files)
            yield return new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>(path, file.Entries);
    }

    /// <summary>Persists the cache; failures are non-fatal by design.</summary>
    public void Save()
    {
        if (!_dirty) return;

        try
        {
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            // Write-then-move keeps the previous cache intact if this write is
            // interrupted; the cache is the only history for pruned transcripts.
            var tempPath = _cachePath + ".tmp";
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(
                    stream,
                    new CacheDocument(
                        SchemaVersion,
                        _files,
                        new ArchivedUsageDocument(
                            _archivedEntryIds.ToList(),
                            _archivedInputTokens,
                            _archivedOutputTokens,
                            _archivedFirstUsageUtc)));
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, _cachePath, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The dashboard still works; the next scan just reparses more files.
        }
    }

    private sealed record CachedFile(
        [property: JsonPropertyName("len")] long Length,
        [property: JsonPropertyName("mtime")] DateTime LastWriteUtc,
        [property: JsonPropertyName("entries")] List<TokenUsageEntry> Entries,
        [property: JsonPropertyName("parserRevision")] int ParserRevision = 0);

    private sealed record CacheDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("files")] Dictionary<string, CachedFile> Files,
        [property: JsonPropertyName("archived")] ArchivedUsageDocument? Archived = null);

    private sealed record ArchivedUsageDocument(
        [property: JsonPropertyName("ids")] List<long>? EntryIds,
        [property: JsonPropertyName("in")] decimal TotalInputTokens,
        [property: JsonPropertyName("out")] decimal TotalOutputTokens,
        [property: JsonPropertyName("first")] DateTime? FirstUsageUtc);
}

public sealed record ArchivedTokenUsage(
    decimal TotalInputTokens,
    decimal TotalOutputTokens,
    DateTime? FirstUsageUtc);
