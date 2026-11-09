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
        Dictionary<string, AdvancedPropertyValue>? advanced = null;

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

        if (adapter is not null && defs.Any(d => d.Backend == TweakBackend.Registry
                && d.PathTemplate is not null
                && d.PathTemplate.Contains("{AdapterClassKey}", StringComparison.Ordinal)))
        {
            try { advanced = await ReadAdvancedPropertiesAsync(adapter, cancellationToken); }
            catch (Exception ex) { _log.Warn($"Get-NetAdapterAdvancedProperty: {ex.Message}"); }
        }

        foreach (var def in defs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[def.Id] = ReadOne(def, adapter, rss, netIp, offload, advanced);
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
        Dictionary<string, string?>? offload,
        Dictionary<string, AdvancedPropertyValue>? advanced)
    {
        try
        {
            return def.Backend switch
            {
                TweakBackend.Registry => ReadRegistry(def, adapter, advanced),
                TweakBackend.NetAdapterRss => ReadRss(def, rss),
                TweakBackend.NetOffloadGlobal => ReadOffloadValue(def, offload),
                TweakBackend.NetIpInterface => ReadNetIp(def, netIp),
                _ => UnsetCurrent(def)
            };
        }
        catch (Exception ex)
        {
            var fallback = UnsetCurrent(def);
            fallback.ValidationError = ex.Message;
            return fallback;
        }
    }

    private TweakCurrentValue ReadRegistry(
        TweakDefinition def,
        AdapterInfo? adapter,
        Dictionary<string, AdvancedPropertyValue>? advanced)
    {
        string path;
        try
        {
            path = PathTemplates.Resolve(def.PathTemplate, adapter);
        }
        catch
        {
            return NotAvailable(def);
        }

        var driverDefaultRaw = TryGetNdiDefault(adapter, def.ValueName);
        var driverDefaultDisplay = driverDefaultRaw is null ? null : MapToDisplay(def, driverDefaultRaw);
        var ndiDeclared = NdiParamExists(adapter, def.ValueName);
        var isAdapterClassKey = adapter?.ClassKeyPath is not null
            && path.StartsWith(adapter.ClassKeyPath, StringComparison.OrdinalIgnoreCase);
        var adv = FindAdvanced(advanced, def.ValueName);

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
                    DisplayValue = enabledOpt?.Display ?? CurrentFallbackDisplay(def),
                    DriverDefaultDisplay = driverDefaultDisplay ?? enabledOpt?.Display
                };
            }

            // Live AdvancedProperty (what Device Manager / spinoffs show)
            if (adv is not null && !string.IsNullOrWhiteSpace(adv.RegistryValue))
            {
                var rawAdv = adv.RegistryValue!;
                return new TweakCurrentValue
                {
                    TweakId = def.Id,
                    IsNotSet = true,
                    RawValue = rawAdv,
                    DisplayValue = MapToDisplay(def, rawAdv, adv.DisplayValue),
                    DriverDefaultDisplay = driverDefaultDisplay ?? MapToDisplay(def, rawAdv, adv.DisplayValue)
                };
            }

            // Driver Ndi\Params default
            if (driverDefaultRaw is not null)
            {
                return new TweakCurrentValue
                {
                    TweakId = def.Id,
                    IsNotSet = true,
                    RawValue = driverDefaultRaw,
                    DisplayValue = driverDefaultDisplay!,
                    DriverDefaultDisplay = driverDefaultDisplay
                };
            }

            // Adapter keyword the driver never declared → N/A
            if (isAdapterClassKey && !def.NotDriverDeclared && !ndiDeclared && adv is null)
                return NotAvailable(def);

            // Global / OS keys with no registry value → well-known effective default
            var implicitRaw = ImplicitOsDefault(def);
            if (implicitRaw is not null)
            {
                return new TweakCurrentValue
                {
                    TweakId = def.Id,
                    IsNotSet = true,
                    RawValue = implicitRaw,
                    DisplayValue = MapToDisplay(def, implicitRaw),
                    DriverDefaultDisplay = MapToDisplay(def, implicitRaw)
                };
            }

            return UnsetCurrent(def, driverDefaultDisplay);
        }

        var raw = _registry.GetValueAsString(path, def.ValueName!) ?? "";
        var display = MapToDisplay(def, raw, adv?.DisplayValue);
        return new TweakCurrentValue
        {
            TweakId = def.Id,
            RawValue = raw,
            DisplayValue = display,
            DriverDefaultDisplay = driverDefaultDisplay
        };
    }

    private static TweakCurrentValue UnsetCurrent(TweakDefinition def, string? driverDefaultDisplay = null)
    {
        var display = driverDefaultDisplay ?? CurrentFallbackDisplay(def);
        return new TweakCurrentValue
        {
            TweakId = def.Id,
            IsNotSet = true,
            RawValue = def.Options.FirstOrDefault(o => o.Display.Equals(display, StringComparison.OrdinalIgnoreCase))?.Value,
            DisplayValue = display,
            DriverDefaultDisplay = driverDefaultDisplay ?? display
        };
    }

    private static string CurrentFallbackDisplay(TweakDefinition def)
    {
        if (def.Options.Count > 0)
            return def.Options[0].Display;
        return ImplicitOsDefault(def) ?? "";
    }

    private static string? ImplicitOsDefault(TweakDefinition def) => def.ValueName switch
    {
        "TcpAckFrequency" => "2",
        "RssBaseCpu" => "0",
        "NetworkThrottlingIndex" => "10",
        "CongestionProvider" => "Default",
        "EnableLoopbackLargeMtu" => "1",
        _ => null
    };

    private sealed record AdvancedPropertyValue(string? RegistryValue, string? DisplayValue);

    private static AdvancedPropertyValue? FindAdvanced(
        Dictionary<string, AdvancedPropertyValue>? advanced,
        string? valueName)
    {
        if (advanced is null || string.IsNullOrWhiteSpace(valueName)) return null;
        foreach (var name in NdiNameCandidates(valueName))
        {
            if (advanced.TryGetValue(name, out var hit))
                return hit;
        }
        return null;
    }

    private static TweakCurrentValue NotAvailable(TweakDefinition def) => new()
    {
        TweakId = def.Id,
        IsNotSet = true,
        IsNotAvailable = true,
        DisplayValue = "N/A for this adapter"
    };

    private bool NdiParamExists(AdapterInfo? adapter, string? valueName)
    {
        if (adapter?.ClassKeyPath is null || string.IsNullOrWhiteSpace(valueName))
            return false;

        foreach (var name in NdiNameCandidates(valueName))
        {
            if (_registry.KeyExists($@"{adapter.ClassKeyPath}\Ndi\Params\{name}"))
                return true;
        }

        return false;
    }

    private string? TryGetNdiDefault(AdapterInfo? adapter, string? valueName)
    {
        if (adapter?.ClassKeyPath is null || string.IsNullOrWhiteSpace(valueName))
            return null;

        foreach (var name in NdiNameCandidates(valueName))
        {
            var paramPath = $@"{adapter.ClassKeyPath}\Ndi\Params\{name}";
            var d = _registry.GetValueAsString(paramPath, "default")
                    ?? _registry.GetValueAsString(paramPath, "Default");
            if (!string.IsNullOrWhiteSpace(d))
                return d;
        }

        return null;
    }

    private static IEnumerable<string> NdiNameCandidates(string valueName)
    {
        yield return valueName;
        if (valueName.StartsWith('*'))
            yield return valueName[1..];
        else
            yield return "*" + valueName;
    }

    private static TweakCurrentValue ReadRss(TweakDefinition def, RssSettings? rss)
    {
        if (rss is null)
            return UnsetCurrent(def);

        if (def.RssProperty == "Enabled")
        {
            var enabled = rss.Enabled == true;
            return new TweakCurrentValue
            {
                TweakId = def.Id,
                RawValue = enabled ? "true" : "false",
                DisplayValue = enabled ? "Enable" : "Disable"
            };
        }

        string? raw = def.RssProperty switch
        {
            "NumberOfReceiveQueues" => rss.NumberOfReceiveQueues?.ToString(),
            "Profile" => rss.Profile,
            "BaseProcessorNumber" => rss.BaseProcessorNumber?.ToString(),
            "MaxProcessorNumber" => rss.MaxProcessorNumber?.ToString(),
            "MaxProcessors" => rss.MaxProcessors?.ToString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw))
            return UnsetCurrent(def);

        var trimmed = raw.Trim();
        return new TweakCurrentValue
        {
            TweakId = def.Id,
            RawValue = trimmed,
            DisplayValue = MapToDisplay(def, trimmed)
        };
    }

    private static TweakCurrentValue ReadOffloadValue(TweakDefinition def, Dictionary<string, string?>? offload)
    {
        if (offload is null || def.OffloadProperty is null
            || !offload.TryGetValue(def.OffloadProperty, out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return UnsetCurrent(def);
        }

        var trimmed = raw.Trim();
        return new TweakCurrentValue
        {
            TweakId = def.Id,
            RawValue = trimmed,
            DisplayValue = MapToDisplay(def, trimmed)
        };
    }

    private static TweakCurrentValue ReadNetIp(TweakDefinition def, NetIpInterfaceSnapshot? snap)
    {
        if (snap is null || def.NetIpProperty is null ||
            !snap.Properties.TryGetValue(def.NetIpProperty, out var raw) || string.IsNullOrWhiteSpace(raw))
            return UnsetCurrent(def);
        var trimmed = raw.Trim();
        return new TweakCurrentValue
        {
            TweakId = def.Id,
            RawValue = trimmed,
            DisplayValue = MapToDisplay(def, trimmed)
        };
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

    private async Task<Dictionary<string, AdvancedPropertyValue>> ReadAdvancedPropertiesAsync(
        AdapterInfo adapter,
        CancellationToken cancellationToken)
    {
        var escaped = adapter.InterfaceAlias.Replace("'", "''");
        var json = await _ps.RunAsync($@"
$ErrorActionPreference='Continue'
Get-NetAdapterAdvancedProperty -Name '{escaped}' -AllProperties -ErrorAction SilentlyContinue |
  Select-Object RegistryKeyword, DisplayValue, RegistryValue |
  ConvertTo-Json -Compress
", cancellationToken);

        var dict = new Dictionary<string, AdvancedPropertyValue>(StringComparer.OrdinalIgnoreCase);
        var payload = ExtractJsonPayload(json);
        if (payload is null) return dict;

        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        var root = doc.RootElement;
        IEnumerable<System.Text.Json.JsonElement> items = root.ValueKind == System.Text.Json.JsonValueKind.Array
            ? root.EnumerateArray()
            : [root];

        foreach (var item in items)
        {
            if (!item.TryGetProperty("RegistryKeyword", out var kwEl)) continue;
            var keyword = kwEl.GetString();
            if (string.IsNullOrWhiteSpace(keyword)) continue;

            string? display = item.TryGetProperty("DisplayValue", out var dv) && dv.ValueKind != System.Text.Json.JsonValueKind.Null
                ? dv.ToString()
                : null;
            string? reg = null;
            if (item.TryGetProperty("RegistryValue", out var rv))
            {
                if (rv.ValueKind == System.Text.Json.JsonValueKind.Array && rv.GetArrayLength() > 0)
                    reg = rv[0].ToString();
                else if (rv.ValueKind != System.Text.Json.JsonValueKind.Null)
                    reg = rv.ToString();
            }

            if (string.IsNullOrWhiteSpace(reg) && string.IsNullOrWhiteSpace(display)) continue;
            dict[keyword] = new AdvancedPropertyValue(
                string.IsNullOrWhiteSpace(reg) ? null : reg.Trim(),
                string.IsNullOrWhiteSpace(display) ? null : display.Trim());
        }

        return dict;
    }

    private async Task<Dictionary<string, string?>> ReadOffloadAsync(CancellationToken cancellationToken)
    {
        // Same source as St1icky (Get-NetOffloadGlobalSetting | select -expand <Property>).
        // Per-property try so one missing enum does not blank Chimney/RSS/etc.
        var json = await _ps.RunAsync(@"
$ErrorActionPreference='Continue'
$g = Get-NetOffloadGlobalSetting
function Get-OffloadProp([string]$name) {
  try {
    $v = $g | Select-Object -ExpandProperty $name -ErrorAction Stop
    if ($null -eq $v) { return $null }
    return [string]$v
  } catch { return $null }
}
$o = [ordered]@{
  ReceiveSideScaling = (Get-OffloadProp 'ReceiveSideScaling')
  ReceiveSegmentCoalescing = (Get-OffloadProp 'ReceiveSegmentCoalescing')
  Chimney = (Get-OffloadProp 'Chimney')
  TaskOffload = (Get-OffloadProp 'TaskOffload')
  NetworkDirect = (Get-OffloadProp 'NetworkDirect')
  NetworkDirectAcrossIPSubnets = (Get-OffloadProp 'NetworkDirectAcrossIPSubnets')
  PacketCoalescingFilter = (Get-OffloadProp 'PacketCoalescingFilter')
}
$o | ConvertTo-Json -Compress
", cancellationToken);

        var payload = ExtractJsonObject(json)
            ?? throw new InvalidOperationException("Get-NetOffloadGlobalSetting returned no JSON.");

        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind == System.Text.Json.JsonValueKind.Null)
                dict[p.Name] = null;
            else
            {
                var s = p.Value.ToString();
                dict[p.Name] = string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            }
        }

        return dict;
    }

    private static string? ExtractJsonObject(string text) => ExtractJsonPayload(text, preferArray: false);

    private static string? ExtractJsonPayload(string text, bool preferArray = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var arrStart = text.IndexOf('[');
        var objStart = text.IndexOf('{');
        if (preferArray && arrStart >= 0 && (objStart < 0 || arrStart < objStart))
        {
            var arrEnd = text.LastIndexOf(']');
            if (arrEnd > arrStart) return text[arrStart..(arrEnd + 1)];
        }

        if (objStart < 0) return null;
        var objEnd = text.LastIndexOf('}');
        if (objEnd <= objStart) return null;
        return text[objStart..(objEnd + 1)];
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

    private static string MapToDisplay(TweakDefinition def, string raw, string? displayHint = null)
    {
        var match = def.Options.FirstOrDefault(o => o.Value.Equals(raw, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Display;

        if (!string.IsNullOrWhiteSpace(displayHint))
        {
            var byHint = def.Options.FirstOrDefault(o =>
                o.Display.Equals(displayHint, StringComparison.OrdinalIgnoreCase));
            if (byHint is not null) return byHint.Display;
        }

        // NetIPInterface Store often returns Active/Persistent; UI shows ActiveStore/PersistentStore.
        if (def.NetIpProperty == "Store")
        {
            if (raw.Equals("Active", StringComparison.OrdinalIgnoreCase)) return "ActiveStore";
            if (raw.Equals("Persistent", StringComparison.OrdinalIgnoreCase)) return "PersistentStore";
        }

        return string.IsNullOrWhiteSpace(displayHint) ? raw : displayHint;
    }
}
