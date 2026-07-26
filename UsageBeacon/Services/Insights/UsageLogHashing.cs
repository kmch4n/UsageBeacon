using System.Text;

namespace UsageBeacon.Services.Insights;

/// <summary>Stable 64-bit FNV-1a hashing for deduplication keys.</summary>
internal static class UsageLogHashing
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    internal static long Hash(string value)
    {
        var hash = OffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= Prime;
        }
        return unchecked((long)hash);
    }
}
