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
        var desc = Escape(adapter.InterfaceDescription);
        var props = string.Join(",", Properties.Select(p => $"'{p}'"));
        var json = await _ps.RunAsync($@"
$ErrorActionPreference='Continue'
$i = Get-NetIPInterface -InterfaceAlias '{alias}' -AddressFamily {addressFamily} -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $i) {{
  $ifIndex = (Get-NetAdapter -InterfaceDescription '{desc}' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty ifIndex)
  if ($null -ne $ifIndex) {{
    $i = Get-NetIPInterface -InterfaceIndex $ifIndex -AddressFamily {addressFamily} -ErrorAction SilentlyContinue | Select-Object -First 1
  }}
}}
if ($null -eq $i) {{ throw ""Get-NetIPInterface found no {addressFamily} interface for '{alias}'"" }}
$h = [ordered]@{{}}
foreach ($p in @({props})) {{
  try {{
    $v = $i | Select-Object -ExpandProperty $p -ErrorAction Stop
    $h[$p] = if ($null -eq $v) {{ $null }} else {{ [string]$v }}
  }} catch {{ $h[$p] = $null }}
}}
$h | ConvertTo-Json -Compress
", cancellationToken);

        var payload = ExtractJsonObject(json)
            ?? throw new InvalidOperationException("Get-NetIPInterface returned no JSON.");

        var snapshot = new NetIpInterfaceSnapshot();
        using var doc = JsonDocument.Parse(payload);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Null) continue;
            var s = prop.Value.ToString();
            if (!string.IsNullOrWhiteSpace(s))
                snapshot.Properties[prop.Name] = s.Trim();
        }
        return snapshot;
    }

    private static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text[start..(end + 1)];
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
