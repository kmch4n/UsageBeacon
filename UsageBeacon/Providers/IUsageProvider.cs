using UsageBeacon.Models;

namespace UsageBeacon.Providers;

public interface IUsageProvider
{
    Task<ServiceUsage> FetchAsync(CancellationToken ct = default);
}
