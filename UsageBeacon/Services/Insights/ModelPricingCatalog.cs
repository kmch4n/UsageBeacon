using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using UsageBeacon.Models.Insights;

namespace UsageBeacon.Services.Insights;

/// <summary>USD prices per million tokens for one model.</summary>
public sealed record ModelPricing(
    [property: JsonPropertyName("input")] decimal Input,
    [property: JsonPropertyName("cachedInput")] decimal CachedInput,
    [property: JsonPropertyName("cacheWrite5m")] decimal CacheWrite5m,
    [property: JsonPropertyName("cacheWrite1h")] decimal CacheWrite1h,
    [property: JsonPropertyName("output")] decimal Output);

/// <summary>
/// Model price table used for cost estimation. Built from the embedded
/// <c>Resources/model-pricing.json</c>, optionally overlaid with a user file
/// so prices can be corrected without a new build. Unknown models yield a
/// null cost so the UI can flag incomplete estimates instead of guessing.
/// </summary>
public sealed class ModelPricingCatalog
{
    private readonly Dictionary<string, IReadOnlyList<PricingPeriod>> _models;

    public string AsOf { get; }

    public ModelPricingCatalog(string asOf, IReadOnlyDictionary<string, ModelPricing> models)
        : this(
            asOf,
            models.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<PricingPeriod>)
                    [new PricingPeriod(DateTime.MinValue, pair.Value)],
                StringComparer.OrdinalIgnoreCase))
    {
    }

    private ModelPricingCatalog(
        string asOf,
        IReadOnlyDictionary<string, IReadOnlyList<PricingPeriod>> models)
    {
        AsOf = asOf;
        _models = new Dictionary<string, IReadOnlyList<PricingPeriod>>(
            models,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves pricing by exact model name first, then by the longest
    /// catalog key that is a prefix of the model name at a "-" boundary
    /// (so "gpt-5" matches "gpt-5-codex" but never "gpt-5.5").
    /// </summary>
    public ModelPricing? Resolve(string model)
        => Resolve(model, DateTime.MaxValue);

    /// <summary>Resolves the rate effective at the usage event timestamp.</summary>
    public ModelPricing? Resolve(string model, DateTime timestampUtc)
    {
        var schedule = ResolveSchedule(model);
        if (schedule is null) return null;

        for (var i = schedule.Count - 1; i >= 0; i--)
        {
            if (schedule[i].EffectiveFromUtc <= timestampUtc)
                return schedule[i].Pricing;
        }
        return null;
    }

    private IReadOnlyList<PricingPeriod>? ResolveSchedule(string model)
    {
        if (_models.TryGetValue(model, out var exact)) return exact;

        IReadOnlyList<PricingPeriod>? best = null;
        var bestLength = -1;
        foreach (var (key, schedule) in _models)
        {
            if (key.Length <= bestLength || key.Length >= model.Length) continue;
            if (!model.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (model[key.Length] != '-') continue;
            best = schedule;
            bestLength = key.Length;
        }
        return best;
    }

    /// <summary>Estimated USD cost of one entry, or null for unknown models.</summary>
    public decimal? TryGetCost(TokenUsageEntry entry)
    {
        var pricing = Resolve(entry.Model, entry.TimestampUtc);
        if (pricing is null) return null;
        return (entry.InputTokens * pricing.Input +
                entry.CachedInputTokens * pricing.CachedInput +
                entry.CacheWrite5mTokens * pricing.CacheWrite5m +
                entry.CacheWrite1hTokens * pricing.CacheWrite1h +
                entry.OutputTokens * pricing.Output) / 1_000_000m;
    }

    /// <summary>Loads the embedded table merged with the optional user override file.</summary>
    public static ModelPricingCatalog LoadDefault(string? overridePath = null)
    {
        var catalog = ParseDocument(ReadEmbeddedJson())
            ?? throw new InvalidOperationException("The embedded model pricing table is invalid.");

        overridePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsageBeacon",
            "model-pricing.json");
        return MergeOverride(catalog, overridePath);
    }

    internal static ModelPricingCatalog MergeOverride(ModelPricingCatalog catalog, string overridePath)
    {
        try
        {
            if (!File.Exists(overridePath)) return catalog;
            var overlay = ParseDocument(File.ReadAllText(overridePath));
            if (overlay is null) return catalog;

            var merged = new Dictionary<string, IReadOnlyList<PricingPeriod>>(
                catalog._models, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, schedule) in overlay._models) merged[key] = schedule;
            var asOf = string.IsNullOrEmpty(overlay.AsOf) ? catalog.AsOf : overlay.AsOf;
            return new ModelPricingCatalog(asOf, merged);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken override must never take the dashboard down.
            return catalog;
        }
    }

    internal static ModelPricingCatalog? ParseDocument(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("models", out var modelsElement) ||
                modelsElement.ValueKind != JsonValueKind.Object)
                return null;

            var asOf = root.TryGetProperty("asOf", out var asOfElement) &&
                       asOfElement.ValueKind == JsonValueKind.String
                ? asOfElement.GetString() ?? ""
                : "";
            var models = new Dictionary<string, IReadOnlyList<PricingPeriod>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var modelProperty in modelsElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(modelProperty.Name) ||
                    !TryParseSchedule(modelProperty.Value, out var schedule))
                    return null;
                models[modelProperty.Name] = schedule;
            }
            return new ModelPricingCatalog(asOf, models);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseSchedule(
        JsonElement element,
        out IReadOnlyList<PricingPeriod> schedule)
    {
        schedule = [];
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (!TryParsePricing(element, out var pricing)) return false;
            schedule = [new PricingPeriod(DateTime.MinValue, pricing)];
            return true;
        }
        if (element.ValueKind != JsonValueKind.Array) return false;

        var periods = new List<PricingPeriod>();
        foreach (var periodElement in element.EnumerateArray())
        {
            if (periodElement.ValueKind != JsonValueKind.Object ||
                !periodElement.TryGetProperty("effectiveFrom", out var effectiveElement) ||
                effectiveElement.ValueKind != JsonValueKind.String ||
                !TryParseEffectiveFrom(effectiveElement.GetString(), out var effectiveFromUtc) ||
                !TryParsePricing(periodElement, out var pricing))
                return false;
            periods.Add(new PricingPeriod(effectiveFromUtc, pricing));
        }
        if (periods.Count == 0) return false;

        periods.Sort((left, right) => left.EffectiveFromUtc.CompareTo(right.EffectiveFromUtc));
        if (periods.Zip(periods.Skip(1), (left, right) =>
                left.EffectiveFromUtc == right.EffectiveFromUtc).Any(equal => equal))
            return false;
        schedule = periods;
        return true;
    }

    private static bool TryParsePricing(JsonElement element, out ModelPricing pricing)
    {
        pricing = null!;
        try
        {
            var parsed = element.Deserialize<ModelPricing>();
            if (parsed is null ||
                parsed.Input < 0 ||
                parsed.CachedInput < 0 ||
                parsed.CacheWrite5m < 0 ||
                parsed.CacheWrite1h < 0 ||
                parsed.Output < 0)
                return false;
            pricing = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseEffectiveFrom(string? value, out DateTime effectiveFromUtc)
    {
        if (DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out effectiveFromUtc))
            return true;

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            effectiveFromUtc = parsed.UtcDateTime;
            return true;
        }
        effectiveFromUtc = default;
        return false;
    }

    private static string ReadEmbeddedJson()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/model-pricing.json"))
            ?? throw new InvalidOperationException("The model pricing resource is missing.");
        using var reader = new StreamReader(resource.Stream);
        return reader.ReadToEnd();
    }

    private sealed record PricingPeriod(
        DateTime EffectiveFromUtc,
        ModelPricing Pricing);
}
