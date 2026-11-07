using Kwanteks.NicTweaker.Models;
using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public sealed class TweakEngine : ITweakEngine
{
    private readonly IRegistryService _registry;
    private readonly IRssService _rss;
    private readonly INetIpInterfaceService _netIp;
    private readonly IPowerShellBridge _ps;
    private readonly IBackupService _backup;
    private readonly ILoggingService _log;

    public TweakEngine(
        IRegistryService registry,
        IRssService rss,
        INetIpInterfaceService netIp,
        IPowerShellBridge ps,
        IBackupService backup,
        ILoggingService log)
    {
        _registry = registry;
        _rss = rss;
        _netIp = netIp;
        _ps = ps;
        _backup = backup;
        _log = log;
    }

    public Task<IReadOnlyDictionary<string, TweakCurrentValue>> ReadSectionAsync(
        TweakSection section,
        AdapterInfo? adapter,
        string? addressFamily = null,
        CancellationToken cancellationToken = default) =>
        ReadTweaksAsync(TweakCatalog.ForSection(section), adapter, addressFamily, cancellationToken);

    public async Task<IReadOnlyDictionary<string, TweakCurrentValue>> ReadTweaksAsync(
        IEnumerable<TweakDefinition> definitions,
        AdapterInfo? adapter,
        string? addressFamily = null,
        CancellationToken cancellationToken = default)
    {
        var defs = definitions.ToList();
        var result = new Dictionary<string, TweakCurrentValue>(StringComparer.OrdinalIgnoreCase);

        RssSettings? rss = null;
        NetIpInterfaceSnapshot? netIp = null;
        Dictionary<string, string?>? offload = null;

        if (defs.Any(d => d.Backend == TweakBackend.NetAdapterRss) && adapter is not null)
        {
            try { rss = await _rss.GetAsync(adapter, cancellationToken); }
            catch (Exception ex) { _log.Warn($"Get-NetAdapterRss: {ex.Message}"); }
        }

        if (defs.Any(d => d.Backend == TweakBackend.NetIpInterface) && adapter is not null && !string.IsNullOrWhiteSpace(addressFamily))
        {
            try { netIp = await _netIp.GetAsync(adapter, addressFamily, cancellationToken); }
            catch (Exception ex) { _log.Warn($"Get-NetIPInterface: {ex.Message}"); }
        }

        if (defs.Any(d => d.Backend == TweakBackend.NetOffloadGlobal))
        {
            try { offload = await ReadOffloadAsync(cancellationToken); }
            catch (Exception ex) { _log.Warn($"Get-NetOffloadGlobalSetting: {ex.Message}"); }
        }

        foreach (var def in defs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[def.Id] = ReadOne(def, adapter, rss, netIp, offload);
        }

        return result;
    }

    public Task<TweakApplyBatchResult> ApplyAsync(
        TweakSection section,
        AdapterInfo? adapter,
        IReadOnlyDictionary<string, string?> desiredValues,
        string? addressFamily = null,
        bool createBackup = true,
        CancellationToken cancellationToken = default) =>
        ApplyTweaksAsync(TweakCatalog.ForSection(section), adapter, desiredValues, addressFamily, createBackup, cancellationToken);

    public async Task<TweakApplyBatchResult> ApplyTweaksAsync(
        IEnumerable<TweakDefinition> definitions,
        AdapterInfo? adapter,
        IReadOnlyDictionary<string, string?> desiredValues,
        string? addressFamily = null,
        bool createBackup = true,
        CancellationToken cancellationToken = default)
    {
        var batch = new TweakApplyBatchResult();
        var defs = definitions.Where(d => !d.IsReadOnly).ToList();
        var backupEntries = new List<BackupValueEntry>();

        // Snapshot current registry values about to change
        foreach (var def in defs)
        {
            if (!desiredValues.TryGetValue(def.Id, out var desired))
                continue;

            var validation = Validate(def, desired);
            if (validation is not null)
            {
                batch.Items.Add(new TweakApplyItemResult
                {
                    TweakId = def.Id,
                    Label = def.Label,
                    Success = false,
                    Message = validation
                });
                continue;
            }

            if (def.Backend == TweakBackend.Registry && def.PathTemplate is not null && def.ValueName is not null)
            {
                try
                {
                    var path = PathTemplates.Resolve(def.PathTemplate, adapter);
                    var exists = _registry.ValueExists(path, def.ValueName);
                    var previous = exists ? _registry.GetValueAsString(path, def.ValueName) : null;
                    backupEntries.Add(new BackupValueEntry
                    {
                        TweakId = def.Id,
                        Path = path,
                        ValueName = def.ValueName,
                        ValueType = def.ValueType.ToString(),
                        PreviousValue = previous,
                        DidNotExist = !exists,
                        NewValue = IsDeleteRequest(def, desired) ? null : ResolveStoredValue(def, desired),
                        Deleted = IsDeleteRequest(def, desired)
                    });
                }
                catch (Exception ex)
                {
                    batch.Items.Add(new TweakApplyItemResult
                    {
                        TweakId = def.Id,
                        Label = def.Label,
                        Success = false,
                        Message = ex.Message
                    });
                }
            }
        }

        if (createBackup && backupEntries.Count > 0)
        {
            var sectionLabel = defs.FirstOrDefault()?.Section.ToString() ?? "Tweaks";
            var snap = new BackupSnapshot
            {
                Id = $"chg-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..36],
                CreatedUtc = DateTimeOffset.UtcNow,
                Label = $"{sectionLabel} apply",
                AdapterGuid = adapter?.InterfaceGuid.ToString(),
                AdapterDescription = adapter?.InterfaceDescription,
                Section = sectionLabel,
                Entries = backupEntries
            };
            batch.Snapshot = await _backup.CreateSnapshotAsync(snap, cancellationToken);
            await _backup.PruneAsync(20, cancellationToken);
        }

        // Apply RSS as a group when possible
        var rssDefs = defs.Where(d => d.Backend == TweakBackend.NetAdapterRss && desiredValues.ContainsKey(d.Id)).ToList();
        if (rssDefs.Count > 0 && adapter is not null)
        {
            try
            {
                var settings = new RssSettings();
                foreach (var def in rssDefs)
                {
                    var desired = desiredValues[def.Id];
                    var stored = ResolveStoredValue(def, desired);
                    switch (def.RssProperty)
                    {
                        case "Enabled":
                            settings.Enabled = string.Equals(stored, "true", StringComparison.OrdinalIgnoreCase) || stored == "1" ||
                                               string.Equals(desired, "Enable", StringComparison.OrdinalIgnoreCase);
                            if (string.Equals(desired, "Disable", StringComparison.OrdinalIgnoreCase))
                                settings.Enabled = false;
                            break;
                        case "NumberOfReceiveQueues":
                            if (int.TryParse(stored, out var q)) settings.NumberOfReceiveQueues = q;
                            break;
                        case "Profile":
                            settings.Profile = stored;
                            break;
                        case "BaseProcessorNumber":
                            if (int.TryParse(stored, out var b)) settings.BaseProcessorNumber = b;
                            break;
                        case "MaxProcessorNumber":
                            if (int.TryParse(stored, out var m)) settings.MaxProcessorNumber = m;
                            break;
                        case "MaxProcessors":
                            if (int.TryParse(stored, out var mp)) settings.MaxProcessors = mp;
                            break;
                    }
                }

                await _rss.SetAsync(adapter, settings, cancellationToken);
                foreach (var def in rssDefs)
                {
                    batch.Items.Add(new TweakApplyItemResult
                    {
                        TweakId = def.Id,
                        Label = def.Label,
                        Success = true,
                        Message = "Applied",
                        RestartRequirement = def.RequiresRestart
                    });
                    _log.Info($"Applied {def.Label}");
                }
            }
            catch (Exception ex)
            {
                foreach (var def in rssDefs)
                {
                    batch.Items.Add(new TweakApplyItemResult
                    {
                        TweakId = def.Id,
                        Label = def.Label,
                        Success = false,
                        Message = ex.Message
                    });
                }
            }
        }

        foreach (var def in defs)
        {
            if (!desiredValues.TryGetValue(def.Id, out var desired))
                continue;
            if (def.Backend == TweakBackend.NetAdapterRss)
                continue; // already handled
            if (batch.Items.Any(i => i.TweakId == def.Id && !i.Success && i.Message is not null && i.Message.StartsWith("Invalid", StringComparison.Ordinal)))
                continue;
            if (batch.Items.Any(i => i.TweakId == def.Id))
                continue;

            try
            {
                await ApplyOneAsync(def, adapter, desired, addressFamily, cancellationToken);
                batch.Items.Add(new TweakApplyItemResult
                {
                    TweakId = def.Id,
                    Label = def.Label,
                    Success = true,
                    Message = "Applied",
                    RestartRequirement = def.RequiresRestart
                });
                _log.Info($"Applied {def.Label} = {desired}");
            }
            catch (Exception ex)
            {
                batch.Items.Add(new TweakApplyItemResult
                {
                    TweakId = def.Id,
                    Label = def.Label,
                    Success = false,
                    Message = ex.Message
                });
                _log.Error($"Failed {def.Label}: {ex.Message}");
            }
        }

        return batch;
    }

    public string? Validate(TweakDefinition def, string? desiredDisplayOrValue)
    {
        if (IsDeleteRequest(def, desiredDisplayOrValue))
        {
            if (!def.SupportsDelete && def.Backend == TweakBackend.Registry)
                return "This value cannot be deleted.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(desiredDisplayOrValue))
            return def.SupportsDelete ? null : "Value is required.";

        var stored = ResolveStoredValue(def, desiredDisplayOrValue);
        if (def.Options.Count > 0 &&
            !def.AllowFreeText &&
            !def.Options.Any(o =>
                o.Value.Equals(stored, StringComparison.OrdinalIgnoreCase) ||
                o.Display.Equals(desiredDisplayOrValue, StringComparison.OrdinalIgnoreCase)))
        {
            // Presence/delete options with "delete" sentinel
            if (def.Options.Any(o => o.Value.Equals("delete", StringComparison.OrdinalIgnoreCase) &&
                                     o.Display.Equals(desiredDisplayOrValue, StringComparison.OrdinalIgnoreCase)))
                return null;
            return $"Invalid option for {def.Label}.";
        }

        if (def.ValueType is TweakValueType.DWord or TweakValueType.Bool || def.MinValue is not null || def.MaxValue is not null)
        {
            if (def.Backend == TweakBackend.NetAdapterRss && def.RssProperty == "Enabled")
                return null;
            if (def.Backend == TweakBackend.NetOffloadGlobal)
                return null;
            if (def.Options.Any(o => o.Value.Equals("delete", StringComparison.OrdinalIgnoreCase)))
                return null;

            if (!int.TryParse(stored, out var n))
                return $"{def.Label} must be a number.";
            if (def.MinValue is not null && n < def.MinValue)
                return $"{def.Label} must be >= {def.MinValue}.";
            if (def.MaxValue is not null && n > def.MaxValue)
                return $"{def.Label} must be <= {def.MaxValue}.";
        }

        return null;
    }

    private TweakCurrentValue ReadOne(
        TweakDefinition def,
        AdapterInfo? adapter,
        RssSettings? rss,
        NetIpInterfaceSnapshot? netIp,
        Dictionary<string, string?>? offload)
    {
        try
        {
            return def.Backend switch
            {
                TweakBackend.Registry => ReadRegistry(def, adapter),
                TweakBackend.NetAdapterRss => ReadRss(def, rss),
                TweakBackend.NetOffloadGlobal => ReadOffloadValue(def, offload),
                TweakBackend.NetIpInterface => ReadNetIp(def, netIp),
                _ => new TweakCurrentValue { TweakId = def.Id, DisplayValue = def.DefaultDisplay, IsNotSet = true }
            };
        }
        catch (Exception ex)
        {
            return new TweakCurrentValue
            {
                TweakId = def.Id,
                DisplayValue = def.DefaultDisplay,
                IsNotSet = true,
                ValidationError = ex.Message
            };
        }
    }

    private TweakCurrentValue ReadRegistry(TweakDefinition def, AdapterInfo? adapter)
    {
        string path;
        try
        {
            path = PathTemplates.Resolve(def.PathTemplate, adapter);
        }
        catch
        {
            return new TweakCurrentValue
            {
                TweakId = def.Id,
                IsNotSet = true,
                DisplayValue = "N/A for this adapter"
            };
        }

        if (!_registry.ValueExists(path, def.ValueName!))
        {
            // Presence-style options
            if (def.Options.Any(o => o.Value.Equals("delete", StringComparison.OrdinalIgnoreCase)))
            {
                var enabledOpt = def.Options.FirstOrDefault(o => o.Value.Equals("delete", StringComparison.OrdinalIgnoreCase));
                return new TweakCurrentValue
                {
                    TweakId = def.Id,
                    IsNotSet = true,
                    DisplayValue = enabledOpt?.Display ?? def.DefaultDisplay
                };
            }

            // Adapter keyword not exposed by this driver → N/A (vs missing value → Not set)
            if (adapter?.ClassKeyPath is not null &&
                path.StartsWith(adapter.ClassKeyPath, StringComparison.OrdinalIgnoreCase) &&
                !_registry.KeyExists($@"{adapter.ClassKeyPath}\Ndi\Params\{def.ValueName}"))
            {
                return new TweakCurrentValue
                {
                    TweakId = def.Id,
                    IsNotSet = true,
                    DisplayValue = "N/A for this adapter"
                };
            }

            return new TweakCurrentValue
            {
                TweakId = def.Id,
                IsNotSet = true,
                DisplayValue = def.DefaultDisplay
            };
        }

        var raw = _registry.GetValueAsString(path, def.ValueName!) ?? "";
        var display = MapToDisplay(def, raw);
        return new TweakCurrentValue { TweakId = def.Id, RawValue = raw, DisplayValue = display };
    }

    private static TweakCurrentValue ReadRss(TweakDefinition def, RssSettings? rss)
    {
        if (rss is null)
            return new TweakCurrentValue { TweakId = def.Id, IsNotSet = true, DisplayValue = "N/A for this adapter" };

        string? raw = def.RssProperty switch
        {
            "Enabled" => rss.Enabled?.ToString(),
            "NumberOfReceiveQueues" => rss.NumberOfReceiveQueues?.ToString(),
            "Profile" => rss.Profile,
            "BaseProcessorNumber" => rss.BaseProcessorNumber?.ToString(),
            "MaxProcessorNumber" => rss.MaxProcessorNumber?.ToString(),
            "MaxProcessors" => rss.MaxProcessors?.ToString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw))
            return new TweakCurrentValue { TweakId = def.Id, IsNotSet = true, DisplayValue = def.DefaultDisplay };

        if (def.RssProperty == "Enabled")
        {
            var enabled = string.Equals(raw, "True", StringComparison.OrdinalIgnoreCase) || raw == "true";
            return new TweakCurrentValue
            {
                TweakId = def.Id,
                RawValue = enabled ? "true" : "false",
                DisplayValue = enabled ? "Enable" : "Disable"
            };
        }

        return new TweakCurrentValue { TweakId = def.Id, RawValue = raw, DisplayValue = MapToDisplay(def, raw) };
    }

    private static TweakCurrentValue ReadOffloadValue(TweakDefinition def, Dictionary<string, string?>? offload)
    {
        if (offload is null || def.OffloadProperty is null || !offload.TryGetValue(def.OffloadProperty, out var raw) || raw is null)
            return new TweakCurrentValue { TweakId = def.Id, IsNotSet = true, DisplayValue = def.DefaultDisplay };
        return new TweakCurrentValue { TweakId = def.Id, RawValue = raw, DisplayValue = raw };
    }

    private static TweakCurrentValue ReadNetIp(TweakDefinition def, NetIpInterfaceSnapshot? snap)
    {
        if (snap is null || def.NetIpProperty is null ||
            !snap.Properties.TryGetValue(def.NetIpProperty, out var raw) || raw is null)
            return new TweakCurrentValue { TweakId = def.Id, IsNotSet = true, DisplayValue = def.DefaultDisplay };
        return new TweakCurrentValue { TweakId = def.Id, RawValue = raw, DisplayValue = raw };
    }

    private async Task ApplyOneAsync(
        TweakDefinition def,
        AdapterInfo? adapter,
        string? desired,
        string? addressFamily,
        CancellationToken cancellationToken)
    {
        switch (def.Backend)
        {
            case TweakBackend.Registry:
                ApplyRegistry(def, adapter, desired);
                break;
            case TweakBackend.NetOffloadGlobal:
                await ApplyOffloadAsync(def, desired, cancellationToken);
                break;
            case TweakBackend.NetIpInterface:
                if (adapter is null || string.IsNullOrWhiteSpace(addressFamily) || def.NetIpProperty is null)
                    throw new InvalidOperationException("Adapter and address family required.");
                var value = ResolveStoredValue(def, desired) ?? desired ?? "";
                await _netIp.SetPropertyAsync(adapter, addressFamily, def.NetIpProperty, value, cancellationToken);
                break;
            default:
                throw new NotSupportedException(def.Backend.ToString());
        }
    }

    private void ApplyRegistry(TweakDefinition def, AdapterInfo? adapter, string? desired)
    {
        var path = PathTemplates.Resolve(def.PathTemplate, adapter);
        _registry.EnsureKey(path);

        if (IsDeleteRequest(def, desired))
        {
            _registry.DeleteValue(path, def.ValueName!);
            return;
        }

        var stored = ResolveStoredValue(def, desired) ?? throw new InvalidOperationException("Empty value.");
        if (stored.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            _registry.DeleteValue(path, def.ValueName!);
            return;
        }

        if (def.ValueType == TweakValueType.DWord)
        {
            if (!int.TryParse(stored, out var n))
                throw new InvalidOperationException($"Invalid DWORD: {stored}");
            _registry.SetValue(path, def.ValueName!, n, RegistryValueKind.DWord);
        }
        else
        {
            _registry.SetValue(path, def.ValueName!, stored, RegistryValueKind.String);
        }
    }

    private async Task ApplyOffloadAsync(TweakDefinition def, string? desired, CancellationToken cancellationToken)
    {
        var value = ResolveStoredValue(def, desired) ?? desired ?? "";
        await _ps.RunAsync(
            $"$ErrorActionPreference='Stop'; Set-NetOffloadGlobalSetting -{def.OffloadProperty} {value}",
            cancellationToken);
    }

    private async Task<Dictionary<string, string?>> ReadOffloadAsync(CancellationToken cancellationToken)
    {
        var json = await _ps.RunAsync(@"
$ErrorActionPreference='Stop'
$g = Get-NetOffloadGlobalSetting
@{
  ReceiveSideScaling = [string]$g.ReceiveSideScaling
  ReceiveSegmentCoalescing = [string]$g.ReceiveSegmentCoalescing
  Chimney = [string]$g.Chimney
  TaskOffload = [string]$g.TaskOffload
  NetworkDirect = [string]$g.NetworkDirect
  NetworkDirectAcrossIPSubnets = [string]$g.NetworkDirectAcrossIPSubnets
  PacketCoalescingFilter = [string]$g.PacketCoalescingFilter
} | ConvertTo-Json -Compress
", cancellationToken);

        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
            dict[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.Null ? null : p.Value.ToString();
        return dict;
    }

    /// <summary>
    /// Explicit delete logic (fixes script operator-precedence bug):
    /// empty / null / DefaultDisplay / "Not set..." => delete when SupportsDelete;
    /// option Value "delete" => delete.
    /// </summary>
    public static bool IsDeleteRequest(TweakDefinition def, string? desired)
    {
        if (desired is null) return def.SupportsDelete;
        if (string.IsNullOrWhiteSpace(desired)) return def.SupportsDelete;
        if (desired.Equals(def.DefaultDisplay, StringComparison.OrdinalIgnoreCase)) return def.SupportsDelete;
        if (desired.StartsWith("Not set", StringComparison.OrdinalIgnoreCase)) return def.SupportsDelete;

        var match = def.Options.FirstOrDefault(o =>
            o.Display.Equals(desired, StringComparison.OrdinalIgnoreCase) ||
            o.Value.Equals(desired, StringComparison.OrdinalIgnoreCase));
        return match is not null && match.Value.Equals("delete", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveStoredValue(TweakDefinition def, string? desired)
    {
        if (desired is null) return null;
        var match = def.Options.FirstOrDefault(o =>
            o.Display.Equals(desired, StringComparison.OrdinalIgnoreCase) ||
            o.Value.Equals(desired, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Value;

        // "Enable"/"Disable" for RSS
        if (def.RssProperty == "Enabled")
        {
            if (desired.Equals("Enable", StringComparison.OrdinalIgnoreCase)) return "true";
            if (desired.Equals("Disable", StringComparison.OrdinalIgnoreCase)) return "false";
        }

        // Strip "N - Label" prefixes commonly used in adv options when free-matching
        var dash = desired.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0 && int.TryParse(desired[..dash], out _))
            return desired[..dash];

        return desired;
    }

    private static string MapToDisplay(TweakDefinition def, string raw)
    {
        var match = def.Options.FirstOrDefault(o => o.Value.Equals(raw, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Display;

        // NetIPInterface Store often returns Active/Persistent; UI shows ActiveStore/PersistentStore.
        if (def.NetIpProperty == "Store")
        {
            if (raw.Equals("Active", StringComparison.OrdinalIgnoreCase)) return "ActiveStore";
            if (raw.Equals("Persistent", StringComparison.OrdinalIgnoreCase)) return "PersistentStore";
        }

        return raw;
    }
}
