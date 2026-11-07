using System.Text.Json;
using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public sealed class NetIpInterfaceService : INetIpInterfaceService
{
    private static readonly string[] Properties =
    [
        "AdvertiseDefaultRoute", "Advertising", "AutomaticMetric", "ClampMss", "DirectedMacWolPattern",
        "EcnMarking", "ForceArpNdWolPattern", "Forwarding", "IgnoreDefaultRoutes", "ManagedAddressConfiguration",
        "NeighborDiscoverySupported", "NeighborUnreachabilityDetection", "OtherStatefulConfiguration",
        "RouterDiscovery", "Store", "WeakHostReceive", "WeakHostSend", "CurrentHopLimit", "BaseReachableTime",
        "ReachableTime", "DadRetransmitTime", "DadTransmits", "NlMtu", "RetransmitTime"
    ];

    private readonly IPowerShellBridge _ps;

    public NetIpInterfaceService(IPowerShellBridge ps) => _ps = ps;

    public async Task<NetIpInterfaceSnapshot> GetAsync(
        AdapterInfo adapter,
        string addressFamily,
        CancellationToken cancellationToken = default)
    {
        var alias = Escape(adapter.InterfaceAlias);
        var props = string.Join(",", Properties.Select(p => $"'{p}'"));
        var json = await _ps.RunAsync($@"
$ErrorActionPreference='Stop'
$i = Get-NetIPInterface -InterfaceAlias '{alias}' -AddressFamily {addressFamily} -ErrorAction Stop
$h = @{{}}
foreach ($p in @({props})) {{
  try {{ $h[$p] = [string]($i | Select-Object -ExpandProperty $p) }} catch {{ $h[$p] = $null }}
}}
$h | ConvertTo-Json -Compress
", cancellationToken);

        var snapshot = new NetIpInterfaceSnapshot();
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
            snapshot.Properties[prop.Name] = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
        return snapshot;
    }

    public async Task SetPropertyAsync(
        AdapterInfo adapter,
        string addressFamily,
        string propertyName,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (!Properties.Contains(propertyName, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown NetIPInterface property: {propertyName}", nameof(propertyName));

        var alias = Escape(adapter.InterfaceAlias);
        var escapedValue = Escape(value);
        await _ps.RunAsync(
            $"$ErrorActionPreference='Stop'; Set-NetIPInterface -InterfaceAlias '{alias}' -AddressFamily {addressFamily} -{propertyName} '{escapedValue}'",
            cancellationToken);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
