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
/// Detailed entries older than <see cref="RetentionDays"/> are moved into a
/// path-independent archive. The archive retains pricing-neutral usage fields
/// so lifetime costs can be recalculated after a pricing catalog update.
/// Only numeric usage values and model names are persisted — never content.
/// </summary>
public sealed class UsageLogCache
{
    private const int SchemaVersion = 2;
    private const int MigrationParserRevision = int.MinValue;

    /// <summary>Days of parsed history kept with per-file metadata.</summary>
    public const int RetentionDays = 180;

    private readonly string _cachePath;
    private readonly Dictionary<string, CachedFile> _files;
    private readonly Dictionary<long, ArchivedUsageEntry> _archivedEntries;
    private readonly HashSet<long> _unresolvedLegacyEntryIds;
    private decimal _unpricedLegacyInputTokens;
    private decimal _unpricedLegacyOutputTokens;
    private DateTime? _unpricedLegacyFirstUsageUtc;
    private bool _dirty;

    private UsageLogCache(
        string cachePath,
        Dictionary<string, CachedFile> files,
        IEnumerable<ArchivedUsageEntry>? archivedEntries = null,
        LegacyArchivedUsageDocument? legacy = null,
        bool dirty = false)
    {
        _cachePath = cachePath;
        _files = files;
        _archivedEntries = (archivedEntries ?? [])
            .GroupBy(entry => entry.IdHash)
            .ToDictionary(group => group.Key, group => group.First());
        _unresolvedLegacyEntryIds = new HashSet<long>(legacy?.EntryIds ?? []);
        _unpricedLegacyInputTokens = legacy?.TotalInputTokens ?? 0m;
        _unpricedLegacyOutputTokens = legacy?.TotalOutputTokens ?? 0m;
        _unpricedLegacyFirstUsageUtc = legacy?.FirstUsageUtc;
        _dirty = dirty;
    }

    public static UsageLogCache Load(string cachePath)
    {
        try
        {
            if (!File.Exists(cachePath))
                return CreateEmpty(cachePath);

            var json = File.ReadAllText(cachePath);
            using var parsed = JsonDocument.Parse(json);
            if (!parsed.RootElement.TryGetProperty("schemaVersion", out var versionElement) ||
                !versionElement.TryGetInt32(out var schemaVersion))
                return CreateEmpty(cachePath);

            if (schemaVersion == SchemaVersion)
            {
                var document = JsonSerializer.Deserialize<CacheDocument>(json);
                if (document?.Files is null)
                    return CreateEmpty(cachePath);

                return new UsageLogCache(
                    cachePath,
                    WithPathComparer(document.Files),
                    ReadArchivedEntries(document.Archived),
                    document.Archived?.Legacy);
            }

            if (schemaVersion == 1)
            {
                var legacyDocument = JsonSerializer.Deserialize<LegacyCacheDocument>(json);
                if (legacyDocument?.Files is null)
                    return CreateEmpty(cachePath);

                // Existing files are reparsed once so archived v1 ids can be
                // recovered into pricing-neutral v2 entries. Deleted or
                // inaccessible files retain their cached recent entries.
                var files = WithPathComparer(legacyDocument.Files);
                foreach (var path in files.Keys.ToList())
                    files[path] = files[path] with
                    {
                        ParserRevision = MigrationParserRevision,
                    };

                return new UsageLogCache(
                    cachePath,
                    files,
                    legacy: legacyDocument.Archived,
                    dirty: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt cache is rebuilt from the logs on the next scan.
        }

        return CreateEmpty(cachePath);
    }

    /// <summary>
    /// Returns the entries for one log file, reparsing only when the file is
    /// new or its size, write time, or parser revision changed.
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
    /// Moves entries older than the cutoff into a path-independent archive.
    /// Identity hashes prevent later file reparses from counting them twice.
    /// During a v1 migration, matching legacy ids are upgraded to exact entries;
    /// totals whose source logs no longer exist remain explicitly unpriced.
    /// </summary>
    public void ArchiveBefore(DateTime cutoffUtc)
    {
        var seen = new HashSet<long>(_archivedEntries.Keys);
        foreach (var path in _files.Keys.OrderBy(
                     path => path,
                     StringComparer.OrdinalIgnoreCase).ToList())
        {
            var file = _files[path];
            var kept = new List<TokenUsageEntry>(file.Entries.Count);
            foreach (var entry in file.Entries)
            {
                if (seen.Contains(entry.IdHash))
                    continue;

                RecoverLegacyEntry(entry);
                if (!seen.Add(entry.IdHash))
                    continue;

                if (entry.TimestampUtc >= cutoffUtc)
                {
                    kept.Add(entry);
                    continue;
                }

                _archivedEntries[entry.IdHash] = ArchivedUsageEntry.From(entry);
            }

            if (kept.Count == file.Entries.Count)
                continue;

            _files[path] = file with { Entries = kept };
            _dirty = true;
        }
    }

    public ArchivedUsageSnapshot ArchivedUsage => new(
        _archivedEntries.Values
            .OrderBy(entry => entry.TimestampUtc)
            .ThenBy(entry => entry.IdHash)
            .ToList(),
        _unpricedLegacyInputTokens,
        _unpricedLegacyOutputTokens,
        _unpricedLegacyFirstUsageUtc,
        _unresolvedLegacyEntryIds.Count);

    /// <summary>All detailed cached entries, including those of deleted files.</summary>
    public IEnumerable<KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>> AllEntries()
    {
        foreach (var (path, file) in _files)
            yield return new KeyValuePair<string, IReadOnlyList<TokenUsageEntry>>(path, file.Entries);
    }

    /// <summary>Persists the cache; failures are non-fatal by design.</summary>
    public void Save()
    {
        if (!_dirty)
            return;

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
                        CreateArchivedUsageDocument()));
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

    private void RecoverLegacyEntry(TokenUsageEntry entry)
    {
        if (!_unresolvedLegacyEntryIds.Remove(entry.IdHash))
            return;

        var input = (decimal)entry.InputTokens +
                    entry.CachedInputTokens +
                    entry.CacheWrite5mTokens +
                    entry.CacheWrite1hTokens;
        _unpricedLegacyInputTokens = Math.Max(0m, _unpricedLegacyInputTokens - input);
        _unpricedLegacyOutputTokens = Math.Max(
            0m,
            _unpricedLegacyOutputTokens - entry.OutputTokens);

        if (_unresolvedLegacyEntryIds.Count == 0)
        {
            _unpricedLegacyInputTokens = 0m;
            _unpricedLegacyOutputTokens = 0m;
            _unpricedLegacyFirstUsageUtc = null;
        }

        _dirty = true;
    }

    private static UsageLogCache CreateEmpty(string cachePath)
        => new(
            cachePath,
            new Dictionary<string, CachedFile>(StringComparer.OrdinalIgnoreCase));

    private static Dictionary<string, CachedFile> WithPathComparer(
        Dictionary<string, CachedFile> files)
        => new(files, StringComparer.OrdinalIgnoreCase);

    private ArchivedUsageDocument CreateArchivedUsageDocument()
    {
        var models = _archivedEntries.Values
            .Select(entry => entry.Model)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var modelIndexes = models
            .Select((model, index) => (model, index))
            .ToDictionary(
                pair => pair.model,
                pair => pair.index,
                StringComparer.OrdinalIgnoreCase);
        var rows = _archivedEntries.Values
            .OrderBy(entry => entry.TimestampUtc)
            .ThenBy(entry => entry.IdHash)
            .Select(entry => ArchivedUsageRow.From(
                entry,
                modelIndexes[entry.Model]))
            .ToList();

        return new ArchivedUsageDocument(
            models,
            rows,
            new LegacyArchivedUsageDocument(
                _unresolvedLegacyEntryIds
                    .OrderBy(id => id)
                    .ToList(),
                _unpricedLegacyInputTokens,
                _unpricedLegacyOutputTokens,
                _unpricedLegacyFirstUsageUtc));
    }

    private static IReadOnlyList<ArchivedUsageEntry> ReadArchivedEntries(
        ArchivedUsageDocument? document)
    {
        if (document?.Rows is null)
            return [];

        var models = document.Models ?? [];
        var entries = new List<ArchivedUsageEntry>(document.Rows.Count);
        foreach (var row in document.Rows)
        {
            if (row.ModelIndex < 0 || row.ModelIndex >= models.Count)
                throw new JsonException("Archived model index is out of range.");
            entries.Add(row.ToEntry(models[row.ModelIndex]));
        }
        return entries;
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
        [property: JsonPropertyName("models")] List<string>? Models,
        [property: JsonPropertyName("rows")] List<ArchivedUsageRow>? Rows,
        [property: JsonPropertyName("legacy")] LegacyArchivedUsageDocument? Legacy);

    [JsonConverter(typeof(ArchivedUsageRowJsonConverter))]
    private sealed record ArchivedUsageRow(
        long IdHash,
        DateTime TimestampUtc,
        UsageService Service,
        int ModelIndex,
        long InputTokens,
        long CachedInputTokens,
        long CacheWrite5mTokens,
        long CacheWrite1hTokens,
        long OutputTokens)
    {
        public static ArchivedUsageRow From(
            ArchivedUsageEntry entry,
            int modelIndex)
            => new(
                entry.IdHash,
                entry.TimestampUtc,
                entry.Service,
                modelIndex,
                entry.InputTokens,
                entry.CachedInputTokens,
                entry.CacheWrite5mTokens,
                entry.CacheWrite1hTokens,
                entry.OutputTokens);

        public ArchivedUsageEntry ToEntry(string model)
            => new(
                IdHash,
                TimestampUtc,
                Service,
                model,
                InputTokens,
                CachedInputTokens,
                CacheWrite5mTokens,
                CacheWrite1hTokens,
                OutputTokens);
    }

    /// <summary>Stores archive rows as compact positional JSON arrays.</summary>
    private sealed class ArchivedUsageRowJsonConverter : JsonConverter<ArchivedUsageRow>
    {
        public override ArchivedUsageRow Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Archived usage row must be an array.");

            var id = ReadInt64(ref reader);
            var timestamp = ReadDateTime(ref reader);
            var serviceValue = ReadInt32(ref reader);
            var modelIndex = ReadInt32(ref reader);
            var input = ReadNonNegativeInt64(ref reader);
            var cachedInput = ReadNonNegativeInt64(ref reader);
            var cacheWrite5m = ReadNonNegativeInt64(ref reader);
            var cacheWrite1h = ReadNonNegativeInt64(ref reader);
            var output = ReadNonNegativeInt64(ref reader);
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException("Archived usage row has an invalid length.");
            if (!Enum.IsDefined(typeof(UsageService), serviceValue))
                throw new JsonException("Archived usage service is invalid.");

            return new ArchivedUsageRow(
                id,
                timestamp,
                (UsageService)serviceValue,
                modelIndex,
                input,
                cachedInput,
                cacheWrite5m,
                cacheWrite1h,
                output);
        }

        public override void Write(
            Utf8JsonWriter writer,
            ArchivedUsageRow value,
            JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.IdHash);
            writer.WriteStringValue(value.TimestampUtc);
            writer.WriteNumberValue((int)value.Service);
            writer.WriteNumberValue(value.ModelIndex);
            writer.WriteNumberValue(value.InputTokens);
            writer.WriteNumberValue(value.CachedInputTokens);
            writer.WriteNumberValue(value.CacheWrite5mTokens);
            writer.WriteNumberValue(value.CacheWrite1hTokens);
            writer.WriteNumberValue(value.OutputTokens);
            writer.WriteEndArray();
        }

        private static long ReadInt64(ref Utf8JsonReader reader)
        {
            if (!reader.Read() ||
                reader.TokenType != JsonTokenType.Number ||
                !reader.TryGetInt64(out var value))
                throw new JsonException("Archived usage value must be an integer.");
            return value;
        }

        private static int ReadInt32(ref Utf8JsonReader reader)
        {
            if (!reader.Read() ||
                reader.TokenType != JsonTokenType.Number ||
                !reader.TryGetInt32(out var value))
                throw new JsonException("Archived usage value must be an integer.");
            return value;
        }

        private static long ReadNonNegativeInt64(ref Utf8JsonReader reader)
        {
            var value = ReadInt64(ref reader);
            if (value < 0)
                throw new JsonException("Archived token values cannot be negative.");
            return value;
        }

        private static DateTime ReadDateTime(ref Utf8JsonReader reader)
        {
            if (!reader.Read() ||
                reader.TokenType != JsonTokenType.String ||
                !reader.TryGetDateTime(out var value))
                throw new JsonException("Archived usage timestamp is invalid.");
            return value;
        }
    }

    private sealed record LegacyCacheDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("files")] Dictionary<string, CachedFile> Files,
        [property: JsonPropertyName("archived")] LegacyArchivedUsageDocument? Archived = null);

    private sealed record LegacyArchivedUsageDocument(
        [property: JsonPropertyName("ids")] List<long>? EntryIds,
        [property: JsonPropertyName("in")] decimal TotalInputTokens,
        [property: JsonPropertyName("out")] decimal TotalOutputTokens,
        [property: JsonPropertyName("first")] DateTime? FirstUsageUtc);
}

public sealed record ArchivedUsageEntry(
    [property: JsonPropertyName("id")] long IdHash,
    [property: JsonPropertyName("ts")] DateTime TimestampUtc,
    [property: JsonPropertyName("svc")] UsageService Service,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("in")] long InputTokens,
    [property: JsonPropertyName("cin")] long CachedInputTokens,
    [property: JsonPropertyName("w5m")] long CacheWrite5mTokens,
    [property: JsonPropertyName("w1h")] long CacheWrite1hTokens,
    [property: JsonPropertyName("out")] long OutputTokens)
{
    public static ArchivedUsageEntry From(TokenUsageEntry entry)
        => new(
            entry.IdHash,
            entry.TimestampUtc,
            entry.Service,
            entry.Model,
            entry.InputTokens,
            entry.CachedInputTokens,
            entry.CacheWrite5mTokens,
            entry.CacheWrite1hTokens,
            entry.OutputTokens);
}

public sealed record ArchivedUsageSnapshot(
    IReadOnlyList<ArchivedUsageEntry> Entries,
    decimal UnpricedLegacyInputTokens,
    decimal UnpricedLegacyOutputTokens,
    DateTime? UnpricedLegacyFirstUsageUtc,
    int UnresolvedLegacyEntryCount = 0)
{
    public decimal TotalInputTokens
        => UnpricedLegacyInputTokens + Entries.Sum(entry =>
            (decimal)entry.InputTokens +
            entry.CachedInputTokens +
            entry.CacheWrite5mTokens +
            entry.CacheWrite1hTokens);

    public decimal TotalOutputTokens
        => UnpricedLegacyOutputTokens + Entries.Sum(entry => (decimal)entry.OutputTokens);

    public DateTime? FirstUsageUtc
        => Entries.Select(entry => (DateTime?)entry.TimestampUtc)
            .Append(UnpricedLegacyFirstUsageUtc)
            .Where(timestamp => timestamp is not null)
            .Min();

    public bool HasUnpricedLegacyUsage
        => UnresolvedLegacyEntryCount > 0 ||
           UnpricedLegacyInputTokens > 0m ||
           UnpricedLegacyOutputTokens > 0m;
}
