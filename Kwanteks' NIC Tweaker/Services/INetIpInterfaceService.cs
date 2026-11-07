using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public sealed class NetIpInterfaceSnapshot
{
    public Dictionary<string, string?> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface INetIpInterfaceService
{
    Task<NetIpInterfaceSnapshot> GetAsync(AdapterInfo adapter, string addressFamily, CancellationToken cancellationToken = default);
    Task SetPropertyAsync(AdapterInfo adapter, string addressFamily, string propertyName, string value, CancellationToken cancellationToken = default);
}
