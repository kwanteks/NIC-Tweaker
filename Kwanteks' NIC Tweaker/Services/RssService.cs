using System.Text.Json;
using Kwanteks.NicTweaker.Models;
using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public sealed class RssService : IRssService
{
    private readonly IPowerShellBridge _ps;
    private readonly IRegistryService _registry;
    private readonly ILoggingService _log;

    public RssService(IPowerShellBridge ps, IRegistryService registry, ILoggingService log)
    {
        _ps = ps;
        _registry = registry;
        _log = log;
    }

    public async Task<RssSettings> GetAsync(AdapterInfo adapter, CancellationToken cancellationToken = default)
    {
        // Alias is unique per interface; two identical NICs share a description.
        var escaped = Escape(adapter.InterfaceAlias);

        // Get-NetAdapterRss throws "No MSFT_NetAdapterRssSettingData objects found" for any adapter that has no
        // RSS object (Wi-Fi, Bluetooth PAN, tunnels, drivers without RSS). SilentlyContinue turns that into $null,
        // so "unsupported" is a normal answer instead of an error that blanks every RSS row.
        var json = await _ps.RunAsync($@"
$ErrorActionPreference='SilentlyContinue'
$r = @(Get-NetAdapterRss -Name '{escaped}' -ErrorAction SilentlyContinue) | Select-Object -First 1
if ($null -eq $r) {{ '{{""Supported"":false}}' }} else {{
@{{
  Supported = $true
  Enabled = [bool]$r.Enabled
  NumberOfReceiveQueues = if ($null -eq $r.NumberOfReceiveQueues) {{ $null }} else {{ [int]$r.NumberOfReceiveQueues }}
  Profile = if ($null -eq $r.Profile) {{ $null }} else {{ [string]$r.Profile }}
  BaseProcessorNumber = if ($null -eq $r.BaseProcessorNumber) {{ $null }} else {{ [int]$r.BaseProcessorNumber }}
  MaxProcessorNumber = if ($null -eq $r.MaxProcessorNumber) {{ $null }} else {{ [int]$r.MaxProcessorNumber }}
  MaxProcessors = if ($null -eq $r.MaxProcessors) {{ $null }} else {{ [int]$r.MaxProcessors }}
}} | ConvertTo-Json -Compress }}
", cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var supported = !(root.TryGetProperty("Supported", out var sup) && sup.ValueKind == JsonValueKind.False);
        if (!supported)
            return ReadFromRegistry(adapter);

        return new RssSettings
        {
            Supported = true,
            Source = "cmdlet",
            Enabled = root.TryGetProperty("Enabled", out var e) && (e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                ? e.GetBoolean()
                : null,
            NumberOfReceiveQueues = GetInt(root, "NumberOfReceiveQueues"),
            Profile = root.TryGetProperty("Profile", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
            BaseProcessorNumber = GetInt(root, "BaseProcessorNumber"),
            MaxProcessorNumber = GetInt(root, "MaxProcessorNumber"),
            MaxProcessors = GetInt(root, "MaxProcessors")
        };
    }

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    /// <summary>
    /// Windows has no RSS object for this adapter, so read what the driver itself stores under the adapter's class key
    /// (the standardized *RSS keywords). A driver with none of them (e.g. Intel AX211 Wi-Fi) simply has RSS off.
    /// </summary>
    private RssSettings ReadFromRegistry(AdapterInfo adapter)
    {
        var result = new RssSettings { Supported = false, Source = "none", Enabled = false };
        var key = adapter.ClassKeyPath;
        if (string.IsNullOrWhiteSpace(key) || !_registry.KeyExists(key))
            return result;

        int? Int(string name) =>
            _registry.ValueExists(key, name) && int.TryParse(_registry.GetValueAsString(key, name), out var v) ? v : null;

        var rss = Int("*RSS");
        var queues = Int("*NumRssQueues");
        var profile = Int("*RSSProfile") ?? Int("*RssProfile");
        var baseProc = Int("*RssBaseProcNumber");
        var maxProc = Int("*MaxRssProcNumber") ?? Int("*RssMaxProcNumber");
        var maxProcs = Int("*MaxRssProcessors");

        if (rss is null && queues is null && profile is null && baseProc is null && maxProc is null && maxProcs is null)
            return result;

        result.Source = "registry";
        result.Enabled = rss is null ? null : rss != 0;
        result.NumberOfReceiveQueues = queues;
        result.Profile = profile switch
        {
            1 => "Closest",
            2 => "ClosestStatic",
            3 => "NUMAStatic",
            4 => "NUMA",
            5 => "Conservative",
            _ => null
        };
        result.BaseProcessorNumber = baseProc;
        result.MaxProcessorNumber = maxProc;
        result.MaxProcessors = maxProcs;
        return result;
    }

    public async Task SetAsync(AdapterInfo adapter, RssSettings settings, CancellationToken cancellationToken = default)
    {
        var escaped = Escape(adapter.InterfaceAlias);

        // Adapters Windows has no RSS object for cannot be changed with Set-NetAdapterRss.
        var current = await GetAsync(adapter, cancellationToken);
        if (current.Source != "cmdlet")
        {
            var wantsChange = settings.Enabled == true || settings.NumberOfReceiveQueues is not null ||
                              !string.IsNullOrWhiteSpace(settings.Profile) || settings.BaseProcessorNumber is not null ||
                              settings.MaxProcessorNumber is not null || settings.MaxProcessors is not null;
            if (!wantsChange)
                return; // "RSS: Disable" on an adapter that has no RSS is already true.
            throw new InvalidOperationException(
                "Windows has no RSS settings for this adapter (the driver does not expose RSS). Use \"Enable RSS Support\" first.");
        }

        var args = new List<string> { $"-Name '{escaped}'" };
        if (settings.Enabled is not null) args.Add($"-Enabled:${settings.Enabled.Value.ToString().ToLowerInvariant()}");
        if (settings.NumberOfReceiveQueues is not null) args.Add($"-NumberOfReceiveQueues {settings.NumberOfReceiveQueues}");
        if (!string.IsNullOrWhiteSpace(settings.Profile) && !settings.Profile.StartsWith("Not set", StringComparison.OrdinalIgnoreCase)) args.Add($"-Profile {settings.Profile}");
        if (settings.BaseProcessorNumber is not null) args.Add($"-BaseProcessorNumber {settings.BaseProcessorNumber}");
        if (settings.MaxProcessorNumber is not null) args.Add($"-MaxProcessorNumber {settings.MaxProcessorNumber}");
        if (settings.MaxProcessors is not null) args.Add($"-MaxProcessors {settings.MaxProcessors}");

        var full = string.Join(' ', args);
        try
        {
            await _ps.RunAsync($"$ErrorActionPreference='Stop'; Set-NetAdapterRss {full}", cancellationToken);
        }
        catch (Exception ex) when (settings.NumberOfReceiveQueues is not null)
        {
            _log.Warn($"Set-NetAdapterRss with queues failed ({ex.Message}); falling back to registry for queues.");
            var withoutQueues = args.Where(a => !a.StartsWith("-NumberOfReceiveQueues", StringComparison.Ordinal)).ToList();
            await _ps.RunAsync($"$ErrorActionPreference='Stop'; Set-NetAdapterRss {string.Join(' ', withoutQueues)}", cancellationToken);

            if (string.IsNullOrWhiteSpace(adapter.ClassKeyPath))
                throw;
            var path = $"{adapter.ClassKeyPath}\\Ndi\\Params\\*NumRssQueues";
            _registry.EnsureKey(path);
            _registry.SetValue(path, "default", settings.NumberOfReceiveQueues.Value.ToString(), RegistryValueKind.String);
        }
    }

    public Task UnlockQueuesAsync(AdapterInfo adapter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(adapter.ClassKeyPath))
                throw new InvalidOperationException("Adapter class key not resolved.");

            var paramPath = $"{adapter.ClassKeyPath}\\Ndi\\Params\\*NumRssQueues";
            var enumPath = $"{paramPath}\\Enum";
            _registry.EnsureKey(paramPath);
            _registry.EnsureKey(enumPath);

            var existingDefault = _registry.GetValueAsString(paramPath, "default") ?? "4";
            _registry.SetValue(paramPath, "ParamDesc", "Receive Side Scaling Queues", RegistryValueKind.String);
            _registry.SetValue(paramPath, "default", existingDefault, RegistryValueKind.String);
            _registry.SetValue(paramPath, "type", "enum", RegistryValueKind.String);

            for (var i = 1; i <= 12; i++)
                _registry.SetValue(enumPath, i.ToString(), $"{i} Queues", RegistryValueKind.String);
        }, cancellationToken);
    }

    public async Task EnableRssSupportAsync(AdapterInfo adapter, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adapter.ClassKeyPath))
            throw new InvalidOperationException("Adapter class key not resolved.");

        await Task.Run(() =>
        {
            var paramPath = $"{adapter.ClassKeyPath}\\Ndi\\Params\\*RSS";
            var enumPath = $"{paramPath}\\Enum";
            _registry.EnsureKey(paramPath);
            _registry.EnsureKey(enumPath);
            _registry.SetValue(paramPath, "ParamDesc", "Receive Side Scaling", RegistryValueKind.String);
            _registry.SetValue(paramPath, "default", "1", RegistryValueKind.String);
            _registry.SetValue(paramPath, "type", "enum", RegistryValueKind.String);
            _registry.SetValue(enumPath, "0", "Disabled", RegistryValueKind.String);
            _registry.SetValue(enumPath, "1", "Enabled", RegistryValueKind.String);
        }, cancellationToken);

        try
        {
            await SetAsync(adapter, new RssSettings { Enabled = true }, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Warn($"RSS NDI scaffolding written, but Set-NetAdapterRss enable failed: {ex.Message}");
        }
    }

    public Task<IReadOnlyList<string>> GetAvailableQueueOptionsAsync(AdapterInfo adapter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(adapter.ClassKeyPath))
            {
                var enumPath = $"{adapter.ClassKeyPath}\\Ndi\\Params\\*NumRssQueues\\Enum";
                if (_registry.KeyExists(enumPath))
                    result.AddRange(_registry.GetValueNames(enumPath).OrderBy(x => int.TryParse(x, out var n) ? n : int.MaxValue));
            }
            return (IReadOnlyList<string>)result;
        }, cancellationToken);
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
