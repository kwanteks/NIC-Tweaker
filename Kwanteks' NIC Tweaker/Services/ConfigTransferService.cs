using System.Reflection;
using System.Text;
using System.Text.Json;
using Kwanteks.NicTweaker.Models;
using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public sealed class ConfigTransferService : IConfigTransferService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ITweakEngine _engine;
    private readonly IRegistryService _registry;
    private readonly IPowerShellBridge _ps;
    private readonly ILoggingService _log;

    public ConfigTransferService(
        ITweakEngine engine,
        IRegistryService registry,
        IPowerShellBridge ps,
        ILoggingService log)
    {
        _engine = engine;
        _registry = registry;
        _ps = ps;
        _log = log;
    }

    public async Task<NetworkTweakerConfig> BuildExportAsync(
        AdapterInfo adapter,
        string? addressFamily,
        bool windowsDefaultsOnly,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        var defs = TweakCatalog.All.Where(d => !d.IsReadOnly).ToList();
        var live = windowsDefaultsOnly
            ? null
            : await _engine.ReadTweaksAsync(defs, adapter, addressFamily, cancellationToken);

        var tweaks = new List<ConfigTweakEntry>();
        foreach (var def in defs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (windowsDefaultsOnly)
                {
                    tweaks.Add(new ConfigTweakEntry
                    {
                        Id = def.Id,
                        Section = def.Section.ToString(),
                        Label = def.Label,
                        State = "NotSet",
                        ValueKind = def.ValueType.ToString(),
                        Value = null
                    });
                    continue;
                }

                live!.TryGetValue(def.Id, out var cur);
                var display = cur?.DisplayValue;
                var notSet = cur is null || cur.IsNotSet
                             || string.IsNullOrWhiteSpace(display)
                             || display.StartsWith("Not set", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(display, "N/A for this adapter", StringComparison.OrdinalIgnoreCase);
                tweaks.Add(new ConfigTweakEntry
                {
                    Id = def.Id,
                    Section = def.Section.ToString(),
                    Label = def.Label,
                    State = notSet ? "NotSet" : "Set",
                    ValueKind = def.ValueType.ToString(),
                    Value = notSet ? null : (cur!.RawValue ?? display)
                });
            }
            catch (Exception ex)
            {
                _log.Warn($"Export skip '{def.Id}': {ex.Message}");
                tweaks.Add(new ConfigTweakEntry
                {
                    Id = def.Id,
                    Section = def.Section.ToString(),
                    Label = def.Label,
                    State = "NotSet",
                    ValueKind = def.ValueType.ToString(),
                    Value = null
                });
            }
        }

        // Ancillary (NetBT / LMHOSTS / affinity)
        if (windowsDefaultsOnly)
        {
            tweaks.Add(Ancillary(ConfigFormat.IdDisableNetBt, "Services", "Disable NetBT", "NotSet", null));
            tweaks.Add(Ancillary(ConfigFormat.IdDisableNetBios, "Services", "Disable NetBIOS", "NotSet", null));
            tweaks.Add(Ancillary(ConfigFormat.IdDisableLmhosts, "Services", "Disable LMHOSTS", "NotSet", null));
            tweaks.Add(Ancillary(ConfigFormat.IdAffinityMask, "Interrupt", "CPU affinity mask", "NotSet", null));
        }
        else
        {
            try { tweaks.AddRange(CaptureAncillary(adapter)); }
            catch (Exception ex)
            {
                _log.Warn($"Export ancillary skipped: {ex.Message}");
            }
        }

        ConfigAdapterInfo adapterInfo;
        try { adapterInfo = await CaptureAdapterMetaAsync(adapter, cancellationToken); }
        catch (Exception ex)
        {
            _log.Warn($"Export adapter meta skipped: {ex.Message}");
            adapterInfo = new ConfigAdapterInfo
            {
                Description = adapter.InterfaceDescription,
                InterfaceGuid = adapter.InterfaceGuid.ToString(),
                NetCfgInstanceId = adapter.NetCfgInstanceId
            };
        }
        return new NetworkTweakerConfig
        {
            Format = ConfigFormat.Name,
            SchemaVersion = ConfigFormat.SchemaVersion,
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
            ExportedUtc = DateTimeOffset.UtcNow,
            WindowsBuild = Environment.OSVersion.Version.ToString(),
            Label = label,
            Adapter = adapterInfo,
            Tweaks = tweaks
        };
    }

    public async Task SaveAtomicAsync(NetworkTweakerConfig config, string path, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        File.Move(temp, path, overwrite: true);
        _log.Info($"Config exported: {path} ({config.Tweaks.Count} tweaks)");
    }

    public async Task<NetworkTweakerConfig> LoadAndValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("Config file not found.", path);
        if (info.Length > ConfigFormat.MaxBytes)
            throw new InvalidOperationException($"Config file is too large ({info.Length} bytes). Max is {ConfigFormat.MaxBytes / (1024 * 1024)} MB.");

        await using var stream = File.OpenRead(path);
        NetworkTweakerConfig? config;
        try
        {
            config = await JsonSerializer.DeserializeAsync<NetworkTweakerConfig>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON: {ex.Message}");
        }

        if (config is null)
            throw new InvalidOperationException("Config file was empty.");
        if (!string.Equals(config.Format, ConfigFormat.Name, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported format '{config.Format}'. Expected '{ConfigFormat.Name}'.");
        if (config.SchemaVersion > ConfigFormat.SchemaVersion)
            throw new InvalidOperationException(
                $"This config (schema {config.SchemaVersion}) was made by a newer version of the app. Update the app and try again.");
        if (config.SchemaVersion < 1)
            throw new InvalidOperationException($"Unsupported schema version {config.SchemaVersion}.");
        if (config.Tweaks is null)
            throw new InvalidOperationException("Config has no tweaks array.");

        // SECURITY: strip any unexpected payload fields by projecting to known shape only (ids + state + value).
        config.Tweaks = config.Tweaks
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .Select(t => new ConfigTweakEntry
            {
                Id = t.Id.Trim(),
                Section = t.Section ?? "",
                Label = t.Label ?? "",
                State = string.Equals(t.State, "NotSet", StringComparison.OrdinalIgnoreCase) ? "NotSet" : "Set",
                ValueKind = t.ValueKind,
                Value = t.Value
            })
            .ToList();

        return config;
    }

    public async Task<ConfigImportPreview> BuildPreviewAsync(
        NetworkTweakerConfig config,
        AdapterInfo adapter,
        string? addressFamily,
        CancellationToken cancellationToken = default)
    {
        var known = TweakCatalog.All.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        string? warning = null;
        if (config.Adapter is not null)
        {
            var sameGuid = string.Equals(config.Adapter.InterfaceGuid, adapter.InterfaceGuid.ToString(), StringComparison.OrdinalIgnoreCase);
            var sameNet = string.Equals(config.Adapter.NetCfgInstanceId, adapter.NetCfgInstanceId, StringComparison.OrdinalIgnoreCase);
            if (!sameGuid && !sameNet)
            {
                warning =
                    $"Exported from '{config.Adapter.Description}', driver {config.Adapter.DriverProvider} {config.Adapter.DriverVersion}. " +
                    $"Applying to '{adapter.InterfaceDescription}'. Unsupported tweaks will be skipped.";
            }
        }
        var live = await _engine.ReadTweaksAsync(known.Values, adapter, addressFamily, cancellationToken);
        var rows = new List<ConfigDiffRow>();
        var skipped = new List<string>();

        foreach (var entry in config.Tweaks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsAncillaryId(entry.Id))
            {
                var (cur, desired, label, section) = ReadAncillaryCurrent(adapter, entry);
                rows.Add(new ConfigDiffRow
                {
                    Id = entry.Id,
                    Label = label,
                    Section = section,
                    CurrentDisplay = cur,
                    ImportedDisplay = DesiredDisplay(entry),
                    ImportedDesired = desired,
                    Changed = !string.Equals(Normalize(cur), Normalize(DesiredDisplay(entry)), StringComparison.OrdinalIgnoreCase),
                    Selected = true
                });
                continue;
            }

            if (!known.TryGetValue(entry.Id, out var def))
            {
                skipped.Add($"{entry.Id}: unknown id (ignored)");
                continue;
            }

            // Never trust paths from file — only id
            // NotSet → delete via engine (DefaultDisplay / null); never trust file paths.
            var desiredForValidate = entry.State == "NotSet"
                ? (def.SupportsDelete ? def.DefaultDisplay : null)
                : (entry.Value ?? "");

            if (entry.State == "NotSet" && !def.SupportsDelete && def.Backend == TweakBackend.Registry)
            {
                skipped.Add($"{def.Label}: cannot delete (not supported)");
                continue;
            }

            var validation = _engine.Validate(def, desiredForValidate);
            if (validation is not null)
            {
                skipped.Add($"{def.Label}: invalid value ({validation})");
                continue;
            }

            live.TryGetValue(def.Id, out var curVal);
            var currentDisplay = curVal is null || curVal.IsNotSet
                ? "(not set)"
                : (curVal.DisplayValue ?? curVal.RawValue ?? "(not set)");
            if (string.Equals(currentDisplay, "N/A for this adapter", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add($"{def.Label}: not supported on this adapter");
                continue;
            }

            var importedDisplay = entry.State == "NotSet" ? "(not set)" : (entry.Value ?? "");
            var changed = !string.Equals(Normalize(currentDisplay), Normalize(importedDisplay), StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(Normalize(curVal?.RawValue), Normalize(entry.Value), StringComparison.OrdinalIgnoreCase);

            rows.Add(new ConfigDiffRow
            {
                Id = def.Id,
                Label = def.Label,
                Section = def.Section.ToString(),
                CurrentDisplay = currentDisplay,
                ImportedDisplay = importedDisplay,
                ImportedDesired = desiredForValidate,
                Changed = changed || (entry.State == "NotSet") != (curVal?.IsNotSet ?? true),
                Selected = true
            });
        }

        return new ConfigImportPreview
        {
            Config = config,
            AdapterWarning = warning,
            Rows = rows,
            Skipped = skipped
        };
    }

    public async Task<ConfigApplyResult> ApplyAsync(
        AdapterInfo adapter,
        string? addressFamily,
        IReadOnlyList<ConfigDiffRow> selectedRows,
        CancellationToken cancellationToken = default)
    {
        var result = new ConfigApplyResult { Skipped = 0 };
        var known = TweakCatalog.All.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var desired = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var ancillary = new List<ConfigDiffRow>();

        foreach (var row in selectedRows.Where(r => r.Selected))
        {
            if (IsAncillaryId(row.Id))
            {
                ancillary.Add(row);
                continue;
            }
            if (!known.ContainsKey(row.Id))
            {
                result.Skipped++;
                result.Details.Add($"{row.Label}: unknown id");
                continue;
            }
            desired[row.Id] = row.ImportedDesired;
        }

        if (desired.Count > 0)
        {
            var batch = await _engine.ApplyTweaksAsync(
                known.Values.Where(d => desired.ContainsKey(d.Id)),
                adapter,
                desired,
                addressFamily,
                createBackup: true,
                cancellationToken);

            foreach (var item in batch.Items)
            {
                if (item.Success)
                {
                    result.Applied++;
                    if (item.RestartRequirement == RestartRequirement.Adapter)
                        result.AdapterRestartRecommended = true;
                    if (item.RestartRequirement == RestartRequirement.Reboot)
                        result.RebootRecommended = true;
                }
                else
                {
                    result.Failed++;
                    result.Details.Add($"{item.Label}: {item.Message}");
                }
            }
        }

        foreach (var row in ancillary)
        {
            try
            {
                ApplyAncillary(adapter, row);
                result.Applied++;
                result.AdapterRestartRecommended = true;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Details.Add($"{row.Label}: {ex.Message}");
            }
        }

        _log.Info($"Config import: {result.Summary}");
        return result;
    }

    private List<ConfigTweakEntry> CaptureAncillary(AdapterInfo adapter)
    {
        var list = new List<ConfigTweakEntry>();
        var netBtStart = _registry.GetValueAsString(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT", "Start");
        var disableNetBt = string.Equals(netBtStart, "4", StringComparison.Ordinal);
        list.Add(Ancillary(ConfigFormat.IdDisableNetBt, "Services", "Disable NetBT", "Set", disableNetBt ? "1" : "0"));

        var lm = _registry.GetValueAsString(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS");
        var disableLm = string.Equals(lm, "0", StringComparison.Ordinal);
        list.Add(Ancillary(ConfigFormat.IdDisableLmhosts, "Services", "Disable LMHOSTS", "Set", disableLm ? "1" : "0"));

        // NetBIOS: NetbiosOptions 2 = disable
        var tcpip = $@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces\Tcpip_{{{adapter.InterfaceGuid}}}";
        var nb = _registry.ValueExists(tcpip, "NetbiosOptions")
            ? _registry.GetValueAsString(tcpip, "NetbiosOptions")
            : null;
        list.Add(Ancillary(ConfigFormat.IdDisableNetBios, "Services", "Disable NetBIOS",
            nb is null ? "NotSet" : "Set",
            nb is null ? null : (string.Equals(nb, "2", StringComparison.Ordinal) ? "1" : "0")));

        if (adapter.InterruptBasePath is not null)
        {
            var path = adapter.InterruptBasePath + @"\Affinity Policy";
            if (_registry.ValueExists(path, "AssignmentSet"))
            {
                var raw = _registry.GetValue(path, "AssignmentSet");
                var mask = raw is int i ? unchecked((uint)i) : 0u;
                list.Add(Ancillary(ConfigFormat.IdAffinityMask, "Interrupt", "CPU affinity mask", "Set", $"0x{mask:X}"));
            }
            else
            {
                list.Add(Ancillary(ConfigFormat.IdAffinityMask, "Interrupt", "CPU affinity mask", "NotSet", null));
            }
        }

        return list;
    }

    private (string current, string? desired, string label, string section) ReadAncillaryCurrent(
        AdapterInfo adapter, ConfigTweakEntry entry)
    {
        var imported = DesiredDisplay(entry);
        return entry.Id switch
        {
            ConfigFormat.IdDisableNetBt => (
                string.Equals(_registry.GetValueAsString(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT", "Start"), "4", StringComparison.Ordinal) ? "1" : "0",
                entry.State == "NotSet" ? "0" : (entry.Value ?? "0"),
                "Disable NetBT", "Services"),
            ConfigFormat.IdDisableLmhosts => (
                string.Equals(_registry.GetValueAsString(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS"), "0", StringComparison.Ordinal) ? "1" : "0",
                entry.State == "NotSet" ? "0" : (entry.Value ?? "0"),
                "Disable LMHOSTS", "Services"),
            ConfigFormat.IdDisableNetBios => (
                ReadNetBiosDisabled(adapter) ? "1" : "0",
                entry.State == "NotSet" ? "0" : (entry.Value ?? "0"),
                "Disable NetBIOS", "Services"),
            ConfigFormat.IdAffinityMask => (
                ReadAffinityHex(adapter),
                entry.State == "NotSet" ? null : entry.Value,
                "CPU affinity mask", "Interrupt"),
            _ => ("?", entry.Value, entry.Label, entry.Section)
        };
    }

    private void ApplyAncillary(AdapterInfo adapter, ConfigDiffRow row)
    {
        switch (row.Id)
        {
            case ConfigFormat.IdDisableNetBt:
                if (row.ImportedDesired == "1")
                    _registry.SetValue(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT", "Start", 4, RegistryValueKind.DWord);
                break;
            case ConfigFormat.IdDisableLmhosts:
                _registry.SetValue(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS",
                    row.ImportedDesired == "1" ? 0 : 1, RegistryValueKind.DWord);
                break;
            case ConfigFormat.IdDisableNetBios:
            {
                var tcpip = $@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces\Tcpip_{{{adapter.InterfaceGuid}}}";
                if (row.ImportedDesired is null)
                    _registry.DeleteValue(tcpip, "NetbiosOptions");
                else
                    _registry.SetValue(tcpip, "NetbiosOptions", row.ImportedDesired == "1" ? 2 : 0, RegistryValueKind.DWord);
                break;
            }
            case ConfigFormat.IdAffinityMask:
            {
                if (adapter.InterruptBasePath is null) return;
                var path = adapter.InterruptBasePath + @"\Affinity Policy";
                if (row.ImportedDesired is null)
                {
                    _registry.DeleteValue(path, "AssignmentSet");
                }
                else
                {
                    var hex = row.ImportedDesired.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
                    var mask = Convert.ToUInt32(hex, 16);
                    _registry.EnsureKey(path);
                    _registry.SetValue(path, "AssignmentSet", unchecked((int)mask), RegistryValueKind.DWord);
                }
                break;
            }
        }
    }

    private bool ReadNetBiosDisabled(AdapterInfo adapter)
    {
        var tcpip = $@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces\Tcpip_{{{adapter.InterfaceGuid}}}";
        return string.Equals(_registry.GetValueAsString(tcpip, "NetbiosOptions"), "2", StringComparison.Ordinal);
    }

    private string ReadAffinityHex(AdapterInfo adapter)
    {
        if (adapter.InterruptBasePath is null) return "(not set)";
        var path = adapter.InterruptBasePath + @"\Affinity Policy";
        if (!_registry.ValueExists(path, "AssignmentSet")) return "(not set)";
        var raw = _registry.GetValue(path, "AssignmentSet");
        var mask = raw is int i ? unchecked((uint)i) : 0u;
        return $"0x{mask:X}";
    }

    private async Task<ConfigAdapterInfo> CaptureAdapterMetaAsync(AdapterInfo adapter, CancellationToken ct)
    {
        var info = new ConfigAdapterInfo
        {
            Description = adapter.InterfaceDescription,
            InterfaceGuid = adapter.InterfaceGuid.ToString(),
            NetCfgInstanceId = adapter.NetCfgInstanceId
        };
        try
        {
            if (!string.IsNullOrWhiteSpace(adapter.PnpDeviceId))
            {
                var escaped = adapter.PnpDeviceId.Replace("'", "''");
                info.DriverVersion = (await _ps.RunAsync(
                    $"(Get-CimInstance Win32_PnPSignedDriver | Where-Object {{ $_.DeviceID -eq '{escaped}' }} | Select-Object -First 1 -ExpandProperty DriverVersion)",
                    ct)).Trim();
                info.DriverProvider = (await _ps.RunAsync(
                    $"(Get-CimInstance Win32_PnPSignedDriver | Where-Object {{ $_.DeviceID -eq '{escaped}' }} | Select-Object -First 1 -ExpandProperty DriverProviderName)",
                    ct)).Trim();
            }
        }
        catch (Exception ex) { _log.Warn($"Config adapter meta: {ex.Message}"); }
        return info;
    }

    private static ConfigTweakEntry Ancillary(string id, string section, string label, string state, string? value) =>
        new() { Id = id, Section = section, Label = label, State = state, ValueKind = "String", Value = value };

    private static bool IsAncillaryId(string id) =>
        id is ConfigFormat.IdDisableNetBt or ConfigFormat.IdDisableNetBios
            or ConfigFormat.IdDisableLmhosts or ConfigFormat.IdAffinityMask;

    private static string DesiredDisplay(ConfigTweakEntry e) =>
        e.State == "NotSet" ? "(not set)" : (e.Value ?? "");

    private static string Normalize(string? s) => (s ?? "").Trim();
}
