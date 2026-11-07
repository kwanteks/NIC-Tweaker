using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public sealed class RssSettings
{
    public bool? Enabled { get; set; }
    public int? NumberOfReceiveQueues { get; set; }
    public string? Profile { get; set; }
    public int? BaseProcessorNumber { get; set; }
    public int? MaxProcessorNumber { get; set; }
    public int? MaxProcessors { get; set; }
}

public interface IRssService
{
    Task<RssSettings> GetAsync(AdapterInfo adapter, CancellationToken cancellationToken = default);
    Task SetAsync(AdapterInfo adapter, RssSettings settings, CancellationToken cancellationToken = default);
    Task UnlockQueuesAsync(AdapterInfo adapter, CancellationToken cancellationToken = default);
    Task EnableRssSupportAsync(AdapterInfo adapter, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailableQueueOptionsAsync(AdapterInfo adapter, CancellationToken cancellationToken = default);
}
