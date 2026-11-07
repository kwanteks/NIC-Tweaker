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
        var escaped = Escape(adapter.InterfaceDescription);
        var json = await _ps.RunAsync($@"
$ErrorActionPreference='Stop'
$r = Get-NetAdapterRss -InterfaceDescription '{escaped}' -ErrorAction Stop
@{{
  Enabled = [bool]$r.Enabled
  NumberOfReceiveQueues = [int]$r.NumberOfReceiveQueues
  Profile = [string]$r.Profile
  BaseProcessorNumber = [int]$r.BaseProcessorNumber
  MaxProcessorNumber = if ($null -eq $r.MaxProcessorNumber) {{ $null }} else {{ [int]$r.MaxProcessorNumber }}
  MaxProcessors = if ($null -eq $r.MaxProcessors) {{ $null }} else {{ [int]$r.MaxProcessors }}
}} | ConvertTo-Json -Compress
", cancellationToken);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RssSettings
        {
            Enabled = root.TryGetProperty("Enabled", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? e.GetBoolean()
                : null,
            NumberOfReceiveQueues = root.TryGetProperty("NumberOfReceiveQueues", out var q) && q.TryGetInt32(out var qi) ? qi : null,
            Profile = root.TryGetProperty("Profile", out var p) ? p.GetString() : null,
            BaseProcessorNumber = root.TryGetProperty("BaseProcessorNumber", out var b) && b.TryGetInt32(out var bi) ? bi : null,
            MaxProcessorNumber = root.TryGetProperty("MaxProcessorNumber", out var m) && m.ValueKind != JsonValueKind.Null && m.TryGetInt32(out var mi) ? mi : null,
            MaxProcessors = root.TryGetProperty("MaxProcessors", out var mp) && mp.ValueKind != JsonValueKind.Null && mp.TryGetInt32(out var mpi) ? mpi : null
        };
    }

    public async Task SetAsync(AdapterInfo adapter, RssSettings settings, CancellationToken cancellationToken = default)
    {
        var escaped = Escape(adapter.InterfaceDescription);
        var args = new List<string> { $"-InterfaceDescription '{escaped}'" };
        if (settings.Enabled is not null) args.Add($"-Enabled:${settings.Enabled.Value.ToString().ToLowerInvariant()}");
        if (settings.NumberOfReceiveQueues is not null) args.Add($"-NumberOfReceiveQueues {settings.NumberOfReceiveQueues}");
        if (!string.IsNullOrWhiteSpace(settings.Profile)) args.Add($"-Profile {settings.Profile}");
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
