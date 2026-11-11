using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Kwanteks.NicTweaker.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class ConfigTransferViewModel : ObservableObject
{
    private readonly IConfigTransferService _config;
    private readonly IRestorePointService _restorePoints;
    private readonly IStorageService _storage;
    private readonly ILoggingService _log;
    private readonly AppSettings _settings;
    private readonly Func<AdapterInfo?> _getAdapter;
    private readonly Func<string> _getAddressFamily;
    private readonly Func<IEnumerable<SectionViewModel>> _getSections;
    private readonly Action<bool, bool, bool> _setServiceChecks;
    private readonly Action<ulong?> _setAffinityMask;
    private readonly Action<string, bool> _setStatus;
    private readonly Action<string> _beginBusy;
    private readonly Action _endBusy;
    private readonly Func<Task> _refreshAll;
    private readonly Func<Task> _restartAdapter;

    [ObservableProperty] private bool _infoBarOpen;
    [ObservableProperty] private string _infoBarTitle = "";
    [ObservableProperty] private string _infoBarMessage = "";
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private string? _lastFilePath;
    [ObservableProperty] private bool _showOpenFolder;
    [ObservableProperty] private bool _showRestartAdapter;

    public ConfigTransferViewModel(
        IConfigTransferService config,
        IRestorePointService restorePoints,
        IStorageService storage,
        ILoggingService log,
        AppSettings settings,
        Func<AdapterInfo?> getAdapter,
        Func<string> getAddressFamily,
        Func<IEnumerable<SectionViewModel>> getSections,
        Action<bool, bool, bool> setServiceChecks,
        Action<ulong?> setAffinityMask,
        Action<string, bool> setStatus,
        Action<string> beginBusy,
        Action endBusy,
        Func<Task> refreshAll,
        Func<Task> restartAdapter)
    {
        _config = config;
        _restorePoints = restorePoints;
        _storage = storage;
        _log = log;
        _settings = settings;
        _getAdapter = getAdapter;
        _getAddressFamily = getAddressFamily;
        _getSections = getSections;
        _setServiceChecks = setServiceChecks;
        _setAffinityMask = setAffinityMask;
        _setStatus = setStatus;
        _beginBusy = beginBusy;
        _endBusy = endBusy;
        _refreshAll = refreshAll;
        _restartAdapter = restartAdapter;
    }

    [RelayCommand]
    private async Task ExportConfigAsync() => await ExportAsync(windowsDefaults: false, quickDesktop: false);

    [RelayCommand]
    private async Task ExportWindowsDefaultsAsync() => await ExportAsync(windowsDefaults: true, quickDesktop: false);

    [RelayCommand]
    private async Task QuickExportDesktopAsync() => await ExportAsync(windowsDefaults: false, quickDesktop: true);

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        var adapter = _getAdapter();
        if (adapter is null) { _setStatus("Select an adapter first.", true); return; }
        var window = App.MainAppWindow;
        if (window is null) return;

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        TrySetPickerFolder(picker, isSave: false);
        picker.FileTypeFilter.Add(".nttweak");
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        RememberFolder(Path.GetDirectoryName(file.Path));
        await ImportFromPathAsync(file.Path);
    }

    public Task ImportFromPathAsync(string path) => ImportCoreAsync(path);

    private async Task ExportAsync(bool windowsDefaults, bool quickDesktop)
    {
        var adapter = _getAdapter();
        if (adapter is null) { _setStatus("Select an adapter first.", true); return; }

        // Pick path first — FileSavePicker fails (often empty COMException) while the busy overlay is up.
        string path;
        if (quickDesktop)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            path = Path.Combine(desktop, SuggestFileName(adapter) + ".nttweak");
            path = AvoidOverwrite(path);
            RememberFolder(desktop);
        }
        else
        {
            var window = App.MainAppWindow;
            if (window is null) return;
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.SuggestedFileName = SuggestFileName(adapter);
            picker.FileTypeChoices.Add("NIC Tweaker config", [".nttweak"]);
            picker.FileTypeChoices.Add("JSON", [".json"]);
            TrySetPickerFolder(picker, isSave: true);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            path = file.Path;
            RememberFolder(Path.GetDirectoryName(path));
        }

        _beginBusy("Building config…");
        try
        {
            var cfg = await _config.BuildExportAsync(adapter, _getAddressFamily(), windowsDefaults);
            await _config.SaveAtomicAsync(cfg, path);
            ShowInfoBar("Config exported", $"{cfg.Tweaks.Count} settings → {path}", InfoBarSeverity.Success, path, restart: false);
            _setStatus($"Exported to {path}", false);
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message)
                ? $"{ex.GetType().Name} (0x{ex.HResult:X8})"
                : ex.Message;
            _log.Error($"Export config: {ex}");
            ShowInfoBar("Export failed", detail, InfoBarSeverity.Error, null, false);
            _setStatus(detail, true);
        }
        finally { _endBusy(); }
    }

    private async Task ImportCoreAsync(string path)
    {
        var adapter = _getAdapter();
        if (adapter is null) { _setStatus("Select an adapter first.", true); return; }
        var root = App.MainAppWindow?.Content?.XamlRoot;
        if (root is null) return;

        _beginBusy("Reading config…");
        ConfigImportPreview preview;
        try
        {
            var cfg = await _config.LoadAndValidateAsync(path);
            preview = await _config.BuildPreviewAsync(cfg, adapter, _getAddressFamily());
        }
        catch (Exception ex)
        {
            ShowInfoBar("Import failed", ex.Message, InfoBarSeverity.Error, null, false);
            _setStatus(ex.Message, true);
            return;
        }
        finally { _endBusy(); }

        var choice = await ConfigTransferDialogs.ShowPreviewAsync(root, preview);
        if (choice is null) return;

        if (choice.LoadUiOnly)
        {
            LoadIntoUi(preview, choice.Selected);
            ShowInfoBar("Loaded into UI", "Values filled — press Apply to write.", InfoBarSeverity.Informational, path, false);
            _setStatus("Config loaded into UI (not written).", false);
            return;
        }

        if (choice.Selected.Count > 1
            && !await SafetyDialogs.ConfirmBulkApplyAsync(
                root,
                $"About to write {choice.Selected.Count} imported settings at once."))
        {
            _setStatus("Import apply cancelled — apply one at a time from the UI if preferred.", false);
            return;
        }

        // Restore point first
        _beginBusy("Creating restore point…");
        try
        {
            var rp = await _restorePoints.CreateAsync("Auto before Import");
            if (!rp.Success)
            {
                _endBusy();
                if (!await ConfigTransferDialogs.ContinueWithoutRestoreAsync(root, rp.Message))
                    return;
            }
        }
        catch (Exception ex)
        {
            _endBusy();
            if (!await ConfigTransferDialogs.ContinueWithoutRestoreAsync(root, ex.Message))
                return;
        }

        _beginBusy("Applying config…");
        try
        {
            var selected = preview.Rows.Where(r => choice.Selected.Contains(r.Id)).ToList();
            foreach (var r in selected) r.Selected = true;
            var result = await _config.ApplyAsync(adapter, _getAddressFamily(), selected);
            await _refreshAll();
            ShowInfoBar("Import finished", result.Summary + (preview.Skipped.Count > 0 ? $" ({preview.Skipped.Count} pre-skipped)" : ""),
                result.Failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                path, result.AdapterRestartRecommended);
            _setStatus(result.Summary, result.Failed > 0);
            if (result.RebootRecommended)
                _setStatus(result.Summary + " Reboot recommended.", true);
        }
        catch (Exception ex)
        {
            ShowInfoBar("Import failed", ex.Message, InfoBarSeverity.Error, null, false);
            _setStatus(ex.Message, true);
        }
        finally { _endBusy(); }
    }

    private void LoadIntoUi(ConfigImportPreview preview, HashSet<string> selectedIds)
    {
        var byId = preview.Rows.Where(r => selectedIds.Contains(r.Id)).ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var section in _getSections())
        {
            foreach (var item in section.Items)
            {
                if (!byId.TryGetValue(item.Definition.Id, out var row)) continue;
                item.SelectedDisplay = row.ImportedDesired;
            }
        }

        var dNetBt = byId.TryGetValue(ConfigFormat.IdDisableNetBt, out var a) && a.ImportedDesired == "1";
        var dNetBios = byId.TryGetValue(ConfigFormat.IdDisableNetBios, out var b) && b.ImportedDesired == "1";
        var dLm = byId.TryGetValue(ConfigFormat.IdDisableLmhosts, out var c) && c.ImportedDesired == "1";
        _setServiceChecks(dNetBt, dNetBios, dLm);

        if (byId.TryGetValue(ConfigFormat.IdAffinityMask, out var aff) && aff.ImportedDesired is not null)
        {
            var hex = aff.ImportedDesired.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            if (ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var mask))
                _setAffinityMask(mask);
        }
        else if (byId.ContainsKey(ConfigFormat.IdAffinityMask))
            _setAffinityMask(null);
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (string.IsNullOrWhiteSpace(LastFilePath) || !File.Exists(LastFilePath)) return;
        ProcessStartExplorerSelect(LastFilePath);
    }

    [RelayCommand]
    private async Task RestartAdapterFromInfoBarAsync() => await _restartAdapter();

    private void ShowInfoBar(string title, string message, InfoBarSeverity sev, string? path, bool restart)
    {
        InfoBarTitle = title;
        InfoBarMessage = message;
        InfoBarSeverity = sev;
        LastFilePath = path;
        ShowOpenFolder = path is not null && File.Exists(path);
        ShowRestartAdapter = restart;
        InfoBarOpen = true;
    }

    private void RememberFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        _settings.LastConfigFolder = folder;
        _storage.SaveSettings(_settings);
    }

    private void TrySetPickerFolder(object picker, bool isSave)
    {
        // WinRT pickers use SuggestedStartLocation=Desktop; LastConfigFolder remembered for next quick export path.
        _ = picker;
        _ = isSave;
        _ = _settings.LastConfigFolder;
    }

    private static string SuggestFileName(AdapterInfo adapter)
    {
        var name = Regex.Replace(adapter.InterfaceDescription ?? "adapter", @"[^\w\-]+", "_").Trim('_');
        if (name.Length > 40) name = name[..40];
        return $"KwanteksNICTweaker_{name}_{DateTime.Now:yyyyMMdd-HHmmss}";
    }

    private static string AvoidOverwrite(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var n = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{baseName}_{n++}{ext}"); }
        while (File.Exists(candidate));
        return candidate;
    }

    private static void ProcessStartExplorerSelect(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }
}
