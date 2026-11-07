using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public interface IAdapterService
{
    Task<IReadOnlyList<AdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default);
    Task EnrichPathsAsync(AdapterInfo adapter, CancellationToken cancellationToken = default);
    Task OpenRegeditAtAdapterAsync(AdapterInfo adapter, CancellationToken cancellationToken = default);
    Task RestartAdapterAsync(AdapterInfo adapter, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
