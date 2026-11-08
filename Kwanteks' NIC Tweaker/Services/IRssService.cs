using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public sealed class RssSettings
{
    /// <summary>
    /// False when Windows has no RSS object for the adapter (Wi-Fi, virtual NICs, drivers without RSS).
    /// Get-NetAdapterRss throws for those; that is "not supported", not a read failure.
    /// </summary>
    public bool Supported { get; set; } = true;

    /// <summary>Where the values came from: "cmdlet", "registry" or "none".</summary>
    public string Source { get; set; } = "cmdlet";

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
