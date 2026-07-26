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
    private readonly Dictionary<string, ModelPricing> _models;

    public string AsOf { get; }

    public ModelPricingCatalog(string asOf, IReadOnlyDictionary<string, ModelPricing> models)
    {
        AsOf = asOf;
        _models = new Dictionary<string, ModelPricing>(models, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves pricing by exact model name first, then by the longest
    /// catalog key that is a prefix of the model name at a "-" boundary
    /// (so "gpt-5" matches "gpt-5-codex" but never "gpt-5.5").
    /// </summary>
    public ModelPricing? Resolve(string model)
    {
        if (_models.TryGetValue(model, out var exact)) return exact;

        ModelPricing? best = null;
        var bestLength = -1;
        foreach (var (key, pricing) in _models)
        {
            if (key.Length <= bestLength || key.Length >= model.Length) continue;
            if (!model.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (model[key.Length] != '-') continue;
            best = pricing;
            bestLength = key.Length;
        }
        return best;
    }

    /// <summary>Estimated USD cost of one entry, or null for unknown models.</summary>
    public decimal? TryGetCost(TokenUsageEntry entry)
    {
        var pricing = Resolve(entry.Model);
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

            var merged = new Dictionary<string, ModelPricing>(
                catalog._models, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, pricing) in overlay._models) merged[key] = pricing;
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
        var document = JsonSerializer.Deserialize<PricingDocument>(json);
        if (document?.Models is null) return null;
        return new ModelPricingCatalog(document.AsOf ?? "", document.Models);
    }

    private static string ReadEmbeddedJson()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/model-pricing.json"))
            ?? throw new InvalidOperationException("The model pricing resource is missing.");
        using var reader = new StreamReader(resource.Stream);
        return reader.ReadToEnd();
    }

    private sealed record PricingDocument(
        [property: JsonPropertyName("asOf")] string? AsOf,
        [property: JsonPropertyName("models")] Dictionary<string, ModelPricing>? Models);
}
