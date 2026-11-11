using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Kwanteks.NicTweaker.Views;
using Microsoft.Win32;
using Windows.ApplicationModel.DataTransfer;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAdapterService _adapters;
    private readonly IRssService _rss;
    private readonly ITweakEngine _engine;
    private readonly IBackupService _backups;
    private readonly IRestorePointService _restorePoints;
    private readonly IConfigTransferService _configTransfer;
    private readonly IStorageService _storage;
    private readonly ILoggingService _log;
    private readonly IRegistryService _registry;
    private CancellationTokenSource? _cts;

    public ObservableCollection<AdapterInfo> Adapters { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<CpuAffinityItem> CpuAffinity { get; } = new();

    public AppSettings Settings { get; }
    public RestorePointViewModel RestorePoint { get; }
    public ConfigTransferViewModel ConfigTransfer { get; }

    public SectionViewModel RssSection { get; }
    public SectionViewModel RssGlobalSection { get; }
    public SectionViewModel InterruptSection { get; }
    public SectionViewModel PowerSection { get; }
    public SectionViewModel GlobalSection { get; }
    public SectionViewModel InterfaceSection { get; }
    public SectionViewModel AdapterFeaturesSection { get; }
    public SectionViewModel BuffersSection { get; }
    public SectionViewModel OffloadSection { get; }
    public SectionViewModel AfdSection { get; }

    public IEnumerable<SectionViewModel> AllSections =>
    [
        RssSection, RssGlobalSection, InterruptSection, PowerSection, GlobalSection,
        InterfaceSection, AdapterFeaturesSection, BuffersSection, OffloadSection, AfdSection
    ];

    [ObservableProperty] private AdapterInfo? _selectedAdapter;
    [ObservableProperty] private string _registryPath = "";
    [ObservableProperty] private string _ndisVersion = "";
    [ObservableProperty] private string _rssStatusText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "Working…";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _showFirstLaunchWarning;
    [ObservableProperty] private bool _ipv4Selected = true;
    [ObservableProperty] private bool _ipv6Selected;
    [ObservableProperty] private bool _opacityReduced;
    [ObservableProperty] private bool _logExpanded;
    [ObservableProperty] private string? _restartBanner;
    public bool HasRestartBanner => !string.IsNullOrWhiteSpace(RestartBanner);
    partial void OnRestartBannerChanged(string? value) => OnPropertyChanged(nameof(HasRestartBanner));
    [ObservableProperty] private bool _rssSupportNeeded;
    [ObservableProperty] private string _affinityHex = "Value: 0x0";
    [ObservableProperty] private bool _disableNetBt;
    [ObservableProperty] private bool _disableNetBios;
    [ObservableProperty] private bool _disableLmhosts;
    [ObservableProperty] private string _themeChoice = "Dark";
    [ObservableProperty] private string _statusAdapterText = "Adapter: (none)";

    public string AddressFamily => Ipv6Selected && !Ipv4Selected ? "IPv6" : "IPv4";

    public MainViewModel(
        IAdapterService adapters,
        IRssService rss,
        ITweakEngine engine,
        IBackupService backups,
        IRestorePointService restorePoints,
        IConfigTransferService configTransfer,
        IStorageService storage,
        ILoggingService log,
        IRegistryService registry)
    {
        _adapters = adapters;
        _rss = rss;
        _engine = engine;
        _backups = backups;
        _restorePoints = restorePoints;
        _configTransfer = configTransfer;
        _storage = storage;
        _log = log;
        _registry = registry;
        Settings = storage.LoadSettings();
        OpacityReduced = Settings.OpacityReduced;
        ThemeChoice = Settings.Theme;
        ShowFirstLaunchWarning = !Settings.FirstLaunchWarningShown;

        _log.EntryAdded += (_, line) =>
        {
            App.CurrentDispatcherQueue?.TryEnqueue(() =>
            {
                LogLines.Insert(0, line);
                while (LogLines.Count > 500) LogLines.RemoveAt(LogLines.Count - 1);
            });
        };

        void Notify(string msg, bool err) { StatusMessage = msg; StatusIsError = err; }
        void OnRestart(RestartRequirement req)
        {
            RestartBanner = req switch
            {
                RestartRequirement.Adapter => "Restart required: restart the adapter.",
                RestartRequirement.Reboot => "Restart required: reboot Windows.",
                _ => null
            };
        }

        RestorePoint = new RestorePointViewModel(
            _restorePoints, _backups, _storage, _log, _registry, Settings,
            () => SelectedAdapter, () => AllSections, Notify, BeginBusy, EndBusy,
            text => BusyText = text);

        ConfigTransfer = new ConfigTransferViewModel(
            _configTransfer, _restorePoints, _storage, _log, Settings,
            () => SelectedAdapter, () => AddressFamily, () => AllSections,
            (netBt, netBios, lm) => { DisableNetBt = netBt; DisableNetBios = netBios; DisableLmhosts = lm; },
            ApplyAffinityMaskToUi,
            Notify, BeginBusy, EndBusy,
            () => RefreshAllAsync(),
            async () => await RestartAdapterCommand.ExecuteAsync(null));

        SectionViewModel Make(string title, IEnumerable<TweakDefinition> defs) =>
            new(title, defs, _engine, () => SelectedAdapter, () => AddressFamily, BeforeApplyAsync, Notify, OnRestart);

        RssSection = Make("RSS Settings", UiGroups.RssSettings);
        RssGlobalSection = Make("RSS Global", UiGroups.RssGlobal);
        InterruptSection = Make("Interrupt Settings", UiGroups.Interrupt);
        PowerSection = Make("PowerSaving Settings", UiGroups.PowerSaving);
        GlobalSection = Make("Global Settings", UiGroups.GlobalSettings);
        InterfaceSection = Make("Interface Settings", UiGroups.InterfaceSettings);
        AdapterFeaturesSection = Make("Adapter Features", UiGroups.AdapterFeatures);
        BuffersSection = Make("Buffers / Interrupts / Coalescing", UiGroups.BuffersCoalesce);
        OffloadSection = Make("Offload Settings", UiGroups.OffloadSettings);
        AfdSection = Make("Tweaks", UiGroups.AfdTweaks);

        var cpuCount = Environment.ProcessorCount;
        for (var i = 0; i < cpuCount; i++)
        {
            var item = new CpuAffinityItem(i);
            item.PropertyChanged += (_, _) => RecalcAffinityHex();
            CpuAffinity.Add(item);
        }
        RecalcAffinityHex();
    }

    public async Task InitializeAsync()
    {
        BeginBusy("Loading adapters…");
        try
        {
            EnsurePresetsOnDisk();
            var list = await _adapters.GetAdaptersAsync();
            Adapters.Clear();
            foreach (var a in list) Adapters.Add(a);
            await EnsureOriginalBackupAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
            _log.Error(ex.Message);
        }
        finally { EndBusy(); }

        if (Adapters.Count > 0)
            SelectedAdapter = Adapters[0];
    }

    private void BeginBusy(string text)
    {
        BusyText = text;
        StatusMessage = text;
        StatusIsError = false;
        IsBusy = true;
    }

    private void EndBusy() => IsBusy = false;

    partial void OnSelectedAdapterChanged(AdapterInfo? value)
    {
        StatusAdapterText = value is null ? "Adapter: (none)" : $"Adapter: {value.InterfaceDescription}";
        _ = OnAdapterChangedAsync(value);
    }

    private bool _addressFamilySync;

    partial void OnIpv4SelectedChanged(bool value)
    {
        if (_addressFamilySync) return;
        if (value)
        {
            _addressFamilySync = true;
            try { Ipv6Selected = false; }
            finally { _addressFamilySync = false; }
        }
        if (!IsBusy)
            _ = InterfaceSection.RefreshAsync();
    }

    partial void OnIpv6SelectedChanged(bool value)
    {
        if (_addressFamilySync) return;
        if (value)
        {
            _addressFamilySync = true;
            try { Ipv4Selected = false; }
            finally { _addressFamilySync = false; }
        }
        if (!IsBusy)
            _ = InterfaceSection.RefreshAsync();
    }

    partial void OnThemeChoiceChanged(string value)
    {
        Settings.Theme = value;
        _storage.SaveSettings(Settings);
        App.ApplyTheme(value);
    }

    private int _adapterLoadGen;

    private async Task OnAdapterChangedAsync(AdapterInfo? adapter)
    {
        var gen = Interlocked.Increment(ref _adapterLoadGen);
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        if (adapter is null) return;

        BeginBusy("Loading adapter settings…");
        try
        {
            await _adapters.EnrichPathsAsync(adapter, token);
            if (gen != _adapterLoadGen) return;
            RegistryPath = adapter.ClassKeyPath ?? "(not found)";
            NdisVersion = adapter.NdisVersion ?? "";
            await LoadInterruptAffinityAsync(adapter, token);
            if (gen != _adapterLoadGen) return;
            await RefreshAllAsync(token);
            if (gen != _adapterLoadGen) return;
            await DetectRssSupportAsync(adapter, token);
            if (gen != _adapterLoadGen) return;
            LoadNetBtChecks();
            StatusMessage = "Ready.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (gen != _adapterLoadGen) return;
            StatusMessage = ex.Message;
            StatusIsError = true;
            _log.Error(ex.Message);
        }
        finally
        {
            if (gen == _adapterLoadGen)
                EndBusy();
        }
    }

    private async Task DetectRssSupportAsync(AdapterInfo adapter, CancellationToken token)
    {
        try
        {
            var rss = await _rss.GetAsync(adapter, token);
            RssStatusText = rss.Enabled is true ? "Enabled" : "Disabled";
            RssSupportNeeded = rss.Source != "cmdlet";
        }
        catch (Exception ex)
        {
            _log.Warn($"RSS read: {ex.Message}");
            RssStatusText = "Not available";
            RssSupportNeeded = true;
        }
    }

    private async Task RefreshAllAsync(CancellationToken token = default)
    {
        foreach (var section in AllSections)
            await section.RefreshAsync(token);
    }

    private async Task<bool> BeforeApplyAsync(int changeCount)
    {
        if (changeCount > 1)
        {
            var root = App.MainAppWindow?.Content?.XamlRoot;
            if (root is not null)
            {
                var proceed = await SafetyDialogs.ConfirmBulkApplyAsync(
                    root,
                    $"About to write {changeCount} settings at once.");
                if (!proceed)
                {
                    StatusMessage = "Bulk apply cancelled — change one setting (or one section) at a time, then Apply.";
                    StatusIsError = false;
                    return false;
                }
            }
        }

        return await RestorePoint.PrepareBeforeApplyAsync(outerOwnsBusy: IsBusy);
    }

    public async Task EnsureSafetyWarningAcceptedAsync()
    {
        if (Settings.FirstLaunchWarningShown) return;
        var root = App.MainAppWindow?.Content?.XamlRoot;
        if (root is null) return;
        // Required gate — keep prompting until they check "I understand" and continue.
        while (!Settings.FirstLaunchWarningShown)
        {
            if (await SafetyDialogs.ShowFirstLaunchAsync(root))
                DismissFirstLaunchWarning();
        }
    }

    private async Task EnsureOriginalBackupAsync()
    {
        await _backups.EnsureOriginalAsync(async () =>
        {
            var entries = new List<BackupValueEntry>();
            foreach (var def in TweakCatalog.All.Where(d =>
                         d.Backend == TweakBackend.Registry &&
                         d.PathTemplate is not null &&
                         d.ValueName is not null &&
                         !d.PathTemplate.Contains('{', StringComparison.Ordinal)))
            {
                var path = def.PathTemplate!;
                var name = def.ValueName!;
                var exists = _registry.ValueExists(path, name);
                entries.Add(new BackupValueEntry
                {
                    TweakId = def.Id,
                    Path = path,
                    ValueName = name,
                    ValueType = def.ValueType.ToString(),
                    PreviousValue = exists ? _registry.GetValueAsString(path, name) : null,
                    DidNotExist = !exists
                });
            }
            return new BackupSnapshot
            {
                Id = "original",
                CreatedUtc = DateTimeOffset.UtcNow,
                Label = "Original state",
                IsOriginal = true,
                Entries = entries
            };
        });
    }

    private void EnsurePresetsOnDisk()
    {
        AppPaths.SeedBundledPresets();
    }

    private void RecalcAffinityHex()
    {
        ulong mask = 0;
        foreach (var c in CpuAffinity.Where(c => c.IsChecked))
            mask |= 1UL << c.Index;
        AffinityHex = $"Value: 0x{mask:X}";
    }

    private Task LoadInterruptAffinityAsync(AdapterInfo adapter, CancellationToken token) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(adapter.InterruptBasePath)) return;
            var path = adapter.InterruptBasePath + @"\Affinity Policy";
            var raw = _registry.GetValueAsString(path, "AssignmentSet");
            ulong mask = 0;
            if (!string.IsNullOrWhiteSpace(raw) &&
                (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? ulong.TryParse(raw.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out mask)
                    : ulong.TryParse(raw, out mask)))
            {
                App.CurrentDispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var c in CpuAffinity)
                        c.IsChecked = ((mask >> c.Index) & 1) == 1;
                    RecalcAffinityHex();
                });
            }
        }, token);

    private void LoadNetBtChecks()
    {
        try
        {
            DisableNetBt = string.Equals(_registry.GetValueAsString(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT", "Start"), "4", StringComparison.Ordinal);
            DisableLmhosts = string.Equals(
                _registry.GetValueAsString(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS"),
                "0", StringComparison.Ordinal);
            DisableNetBios = SelectedAdapter is not null && string.Equals(
                _registry.GetValueAsString(NetBiosPath(SelectedAdapter), "NetbiosOptions"),
                "2", StringComparison.Ordinal);
        }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedAdapter is not null)
            await OnAdapterChangedAsync(SelectedAdapter);
        else
            await InitializeAsync();
    }

    [RelayCommand]
    private async Task OpenRegistryAsync()
    {
        if (SelectedAdapter is null) { StatusMessage = "No adapter selected."; StatusIsError = true; return; }
        try { await _adapters.OpenRegeditAtAdapterAsync(SelectedAdapter); }
        catch (Exception ex) { StatusMessage = ex.Message; StatusIsError = true; }
    }

    [RelayCommand]
    private void CopyRegistryPath()
    {
        if (string.IsNullOrWhiteSpace(RegistryPath)) return;
        var pkg = new DataPackage();
        pkg.SetText(RegistryPath);
        Clipboard.SetContent(pkg);
        StatusMessage = "Registry path copied.";
        StatusIsError = false;
    }

    [RelayCommand]
    private void ToggleOpacity()
    {
        OpacityReduced = !OpacityReduced;
        Settings.OpacityReduced = OpacityReduced;
        _storage.SaveSettings(Settings);
        App.SetWindowOpacity(OpacityReduced);
    }

    [RelayCommand]
    private async Task RestartAdapterAsync()
    {
        if (SelectedAdapter is null) { StatusMessage = "No adapter selected."; StatusIsError = true; return; }
        BeginBusy("Restarting adapter…");
        try
        {
            var progress = new Progress<string>(m => { StatusMessage = m; BusyText = m; });
            await _adapters.RestartAdapterAsync(SelectedAdapter, progress);
            StatusMessage = "Adapter restarted.";
            StatusIsError = false;
            RestartBanner = null;
        }
        catch (Exception ex) { StatusMessage = $"Restart failed: {ex.Message}"; StatusIsError = true; }
        finally { EndBusy(); }

        if (SelectedAdapter is not null)
            await OnAdapterChangedAsync(SelectedAdapter);
    }

    [RelayCommand]
    private async Task UnlockRssQueuesAsync()
    {
        if (SelectedAdapter is null) return;
        BeginBusy("Unlocking RSS queues…");
        try
        {
            await _rss.UnlockQueuesAsync(SelectedAdapter);
            StatusMessage = "RSS queues unlocked (1–12).";
            StatusIsError = false;
            await RssSection.RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; StatusIsError = true; }
        finally { EndBusy(); }
    }

    [RelayCommand]
    private async Task EnableRssSupportAsync()
    {
        if (SelectedAdapter is null) return;
        BeginBusy("Enabling RSS support…");
        try
        {
            await _rss.EnableRssSupportAsync(SelectedAdapter);
            StatusMessage = "RSS support enabled.";
            StatusIsError = false;
        }
        catch (Exception ex) { StatusMessage = ex.Message; StatusIsError = true; }
        finally { EndBusy(); }

        if (SelectedAdapter is not null)
            await OnAdapterChangedAsync(SelectedAdapter);
    }

    [RelayCommand]
    private async Task ApplyAllAsync()
    {
        BeginBusy("Applying all settings…");
        try
        {
            var bulkCount = AllSections.Sum(s => s.Items.Count(i => i.IsEditorEnabled));
            if (!await BeforeApplyAsync(Math.Max(bulkCount, 2)))
            {
                StatusMessage = "Apply All cancelled.";
                StatusIsError = false;
                return;
            }
            RestartRequirement max = RestartRequirement.None;
            var failures = 0;
            foreach (var section in AllSections)
            {
                foreach (var item in section.Items.Where(i => i.IsEditorEnabled))
                    item.Validate(_engine);
                if (section.Items.Any(i => !string.IsNullOrEmpty(i.ValidationError)))
                {
                    failures++;
                    continue;
                }
                var desired = section.Items.Where(i => i.IsEditorEnabled)
                    .ToDictionary(i => i.Definition.Id, i => i.DesiredValue);
                // Restore-point / bulk confirm already done above — skip re-prompt per section.
                var result = await _engine.ApplyTweaksAsync(
                    section.Items.Select(i => i.Definition), SelectedAdapter, desired, AddressFamily);
                failures += result.Items.Count(i => !i.Success && !i.Skipped);
                if (result.MaxRestart > max) max = result.MaxRestart;
            }
            await ApplyNetBtTcpAsync();
            await ApplyAffinityAsync();
            StatusMessage = failures == 0 ? "Apply All completed." : $"Apply All finished with {failures} failure(s).";
            StatusIsError = failures > 0;
            if (max != RestartRequirement.None)
                RestartBanner = max == RestartRequirement.Reboot ? "Restart required: reboot Windows." : "Restart required: restart the adapter.";
            await RefreshAllAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; StatusIsError = true; }
        finally { EndBusy(); }
    }

    [RelayCommand]
    private async Task ApplyInterruptAsync()
    {
        await InterruptSection.ApplyCommand.ExecuteAsync(null);
        await ApplyAffinityAsync();
    }

    [RelayCommand]
    private async Task ApplyAdvancedAsync()
    {
        BeginBusy("Applying advanced settings…");
        try
        {
            await AdapterFeaturesSection.ApplyCommand.ExecuteAsync(null);
            await BuffersSection.ApplyCommand.ExecuteAsync(null);
            await OffloadSection.ApplyCommand.ExecuteAsync(null);
        }
        finally { EndBusy(); }
    }

    [RelayCommand]
    private void RestoreDefaultsUi()
    {
        foreach (var section in AllSections)
            ResetSectionToDefaults(section.Title);
        StatusMessage = "UI reset to defaults (not written until Apply).";
        StatusIsError = false;
    }

    [RelayCommand]
    private void ClearAffinity()
    {
        foreach (var c in CpuAffinity) c.IsChecked = false;
        RecalcAffinityHex();
    }

    private void ApplyAffinityMaskToUi(ulong? mask)
    {
        if (mask is null)
        {
            foreach (var c in CpuAffinity) c.IsChecked = false;
        }
        else
        {
            foreach (var c in CpuAffinity)
                c.IsChecked = (mask.Value & (1UL << c.Index)) != 0;
        }
        RecalcAffinityHex();
    }

    private Task ApplyAffinityAsync() => Task.Run(() =>
    {
        if (SelectedAdapter?.InterruptBasePath is null) return;
        var path = SelectedAdapter.InterruptBasePath + @"\Affinity Policy";
        ulong mask = 0;
        foreach (var c in CpuAffinity.Where(c => c.IsChecked))
            mask |= 1UL << c.Index;
        // Nothing ticked and nothing stored: don't invent an AssignmentSet of 0 on a plain Apply All.
        if (mask == 0 && !_registry.ValueExists(path, "AssignmentSet")) return;
        _registry.EnsureKey(path);
        _registry.SetValue(path, "AssignmentSet", unchecked((int)mask), RegistryValueKind.DWord);
        _log.Info($"Affinity AssignmentSet = 0x{mask:X}");
    });

    private static string NetBiosPath(AdapterInfo adapter) =>
        $@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces\Tcpip_{{{adapter.InterfaceGuid}}}";

    [RelayCommand]
    private async Task ApplyNetBtTcpAsync()
    {
        var adapter = SelectedAdapter;
        await Task.Run(() =>
        {
            try
            {
                if (DisableNetBt)
                    _registry.SetValue(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT", "Start", 4, RegistryValueKind.DWord);
                _registry.SetValue(@"HKLM\SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS",
                    DisableLmhosts ? 0 : 1, RegistryValueKind.DWord);

                // NetBIOS binding is per-adapter; only write when the checkbox changes it (2 = disabled).
                if (adapter is not null)
                {
                    var path = NetBiosPath(adapter);
                    var current = _registry.GetValueAsString(path, "NetbiosOptions");
                    if (DisableNetBios && current != "2")
                        _registry.SetValue(path, "NetbiosOptions", 2, RegistryValueKind.DWord);
                    else if (!DisableNetBios && current == "2")
                        _registry.SetValue(path, "NetbiosOptions", 0, RegistryValueKind.DWord);
                }
                _log.Info("Applied NetBT / LMHOSTS settings.");
            }
            catch (Exception ex) { _log.Error($"NetBT apply: {ex.Message}"); throw; }
        });
        StatusMessage = "NetBT / TCP options applied.";
        StatusIsError = false;
    }

    [RelayCommand]
    private void LoadPreset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Path.Combine(_storage.PresetsDirectory, $"{name.ToLowerInvariant()}.json");
        if (!File.Exists(path))
        {
            StatusMessage = $"Preset not found: {path}";
            StatusIsError = true;
            return;
        }
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("values", out var values))
                throw new InvalidOperationException("Preset has no \"values\" object.");
            foreach (var p in values.EnumerateObject())
                map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preset '{path}' could not be read: {ex.Message}";
            StatusIsError = true;
            return;
        }

        var applied = 0;
        var unsupported = new List<string>();
        var unknown = new List<string>();
        var itemsById = AllSections.SelectMany(s => s.Items)
            .ToDictionary(i => i.Definition.Id, i => i, StringComparer.OrdinalIgnoreCase);

        foreach (var (id, value) in map)
        {
            if (!itemsById.TryGetValue(id, out var item))
            {
                unknown.Add(id);
                continue;
            }
            if (item.IsNotAvailable || !item.IsEditorEnabled)
            {
                unsupported.Add(item.Label);
                continue;
            }
            if (string.IsNullOrWhiteSpace(value)) continue;
            item.EnsureOptionPublic(value);
            item.SelectedDisplay = value;
            applied++;
        }

        var parts = new List<string> { $"Loaded {name} preset: {applied} value(s) into the UI (not written until Apply)." };
        if (unsupported.Count > 0)
            parts.Add($"Not supported by this driver: {string.Join(", ", unsupported)}.");
        if (unknown.Count > 0)
            parts.Add($"Unknown preset keys: {string.Join(", ", unknown)}.");
        StatusMessage = string.Join(" ", parts);
        StatusIsError = unsupported.Count > 0 || unknown.Count > 0;
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        var entries = new List<BackupValueEntry>();
        foreach (var section in AllSections)
        {
            foreach (var item in section.Items.Where(i => i.Definition.Backend == TweakBackend.Registry))
            {
                try
                {
                    var path = PathTemplates.Resolve(item.Definition.PathTemplate, SelectedAdapter);
                    var exists = _registry.ValueExists(path, item.Definition.ValueName!);
                    entries.Add(new BackupValueEntry
                    {
                        TweakId = item.Definition.Id,
                        Path = path,
                        ValueName = item.Definition.ValueName!,
                        ValueType = item.Definition.ValueType.ToString(),
                        PreviousValue = exists ? _registry.GetValueAsString(path, item.Definition.ValueName!) : null,
                        DidNotExist = !exists
                    });
                }
                catch { /* skip unresolved */ }
            }
        }
        var snap = new BackupSnapshot
        {
            Id = $"manual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            CreatedUtc = DateTimeOffset.UtcNow,
            Label = "Manual backup",
            AdapterGuid = SelectedAdapter?.InterfaceGuid.ToString(),
            AdapterDescription = SelectedAdapter?.InterfaceDescription,
            Entries = entries
        };
        await _backups.CreateSnapshotAsync(snap);
        StatusMessage = "Backup saved.";
        StatusIsError = false;
    }

    [RelayCommand]
    private async Task RevertLastAsync()
    {
        var last = await _backups.GetLastChangeAsync();
        if (last is null) { StatusMessage = "No previous change."; StatusIsError = true; return; }
        await _backups.RestoreAsync(last);
        StatusMessage = $"Restored '{last.Label}'.";
        StatusIsError = false;
        await RefreshAllAsync();
    }

    [RelayCommand]
    private void DismissFirstLaunchWarning()
    {
        ShowFirstLaunchWarning = false;
        Settings.FirstLaunchWarningShown = true;
        _storage.SaveSettings(Settings);
    }

    public void ResetSectionToDefaults(string? sectionTitle)
    {
        var section = AllSections.FirstOrDefault(s =>
            string.Equals(s.Title, sectionTitle, StringComparison.OrdinalIgnoreCase));
        if (section is null) return;
        foreach (var item in section.Items.Where(i => i.IsEditorEnabled))
        {
            var fallback = item.EffectiveDefaultDisplay
                           ?? item.Definition.Options.FirstOrDefault()?.Display
                           ?? item.Definition.DefaultDisplay;
            item.SelectedDisplay = fallback;
            item.EnsureOptionPublic(fallback);
        }
        StatusMessage = $"Reset '{section.Title}' UI to defaults (not written until Apply).";
        StatusIsError = false;
    }

    public Task<IReadOnlyList<BackupSnapshot>> ListSnapshotsAsync() => _backups.ListAsync();

    public async Task RestoreSnapshotByIdAsync(string id)
    {
        var snap = await _backups.GetAsync(id);
        if (snap is null) { StatusMessage = "Snapshot not found."; StatusIsError = true; return; }
        await _backups.RestoreAsync(snap);
        StatusMessage = $"Restored '{snap.Label}'.";
        StatusIsError = false;
        await RefreshAllAsync();
    }

    public Task ExportSnapshotAsync(string id, string path) => _backups.ExportAsync(id, path);

    public async Task ImportSnapshotAsync(string path)
    {
        await _backups.ImportAsync(path);
        StatusMessage = "Snapshot imported.";
        StatusIsError = false;
    }

    public IReadOnlyList<string> ThemeChoices { get; } = ["System", "Light", "Dark"];
    public IReadOnlyList<string> SectionTitles => AllSections.Select(s => s.Title).ToList();
}
