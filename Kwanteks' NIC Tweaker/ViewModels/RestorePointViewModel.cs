using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Kwanteks.NicTweaker.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class RestorePointViewModel : ObservableObject
{
    private readonly IRestorePointService _restorePoints;
    private readonly IBackupService _backups;
    private readonly IStorageService _storage;
    private readonly ILoggingService _log;
    private readonly IRegistryService _registry;
    private readonly AppSettings _settings;
    private readonly Func<AdapterInfo?> _getAdapter;
    private readonly Func<IEnumerable<SectionViewModel>> _getSections;
    private readonly Action<string, bool> _setStatus;
    private readonly Action<string> _beginBusy;
    private readonly Action _endBusy;
    private readonly Action<string> _setBusyText;

    private bool _fullWarningShownThisSession;
    private bool _acceptedThisSession;

    [ObservableProperty] private bool _createRestorePointBeforeApply;
    [ObservableProperty] private bool _infoBarOpen;
    [ObservableProperty] private string _infoBarTitle = "";
    [ObservableProperty] private string _infoBarMessage = "";
    [ObservableProperty] private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Informational;
    [ObservableProperty] private bool _showOpenSystemRestore;
    [ObservableProperty] private bool _showEnableProtection;

    public RestorePointViewModel(
        IRestorePointService restorePoints,
        IBackupService backups,
        IStorageService storage,
        ILoggingService log,
        IRegistryService registry,
        AppSettings settings,
        Func<AdapterInfo?> getAdapter,
        Func<IEnumerable<SectionViewModel>> getSections,
        Action<string, bool> setStatus,
        Action<string> beginBusy,
        Action endBusy,
        Action<string> setBusyText)
    {
        _restorePoints = restorePoints;
        _backups = backups;
        _storage = storage;
        _log = log;
        _registry = registry;
        _settings = settings;
        _getAdapter = getAdapter;
        _getSections = getSections;
        _setStatus = setStatus;
        _beginBusy = beginBusy;
        _endBusy = endBusy;
        _setBusyText = setBusyText;
        CreateRestorePointBeforeApply = settings.CreateRestorePointBeforeApply;
    }

    partial void OnCreateRestorePointBeforeApplyChanged(bool value)
    {
        _settings.CreateRestorePointBeforeApply = value;
        _storage.SaveSettings(_settings);
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        var root = GetXamlRoot();
        if (root is null) return;

        if (!await EnsureWarningAcceptedAsync(root))
            return;

        await RunCreateAsync(root, offerContinueWithout: false, manageBusy: true);
    }

    /// <summary>Returns false if the user cancelled apply.</summary>
    /// <param name="outerOwnsBusy">True when Apply All already shows the busy overlay.</param>
    public async Task<bool> PrepareBeforeApplyAsync(bool outerOwnsBusy)
    {
        if (!CreateRestorePointBeforeApply)
            return true;

        var root = GetXamlRoot();
        if (root is null) return true;

        if (!await EnsureWarningAcceptedAsync(root))
            return false;

        return await RunCreateAsync(root, offerContinueWithout: true, manageBusy: !outerOwnsBusy);
    }

    private async Task<bool> EnsureWarningAcceptedAsync(XamlRoot root)
    {
        if (_acceptedThisSession)
            return await RestoreDialogs.ShowShortConfirmAsync(root);

        var showFull = !_fullWarningShownThisSession
                       && (!_settings.DontShowFullRestoreWarning || !_settings.RestoreWarningEverShown);

        if (showFull)
        {
            var result = await RestoreDialogs.ShowFullWarningAsync(root, _restorePoints, _getAdapter);
            if (!result.Accepted) return false;

            _fullWarningShownThisSession = true;
            _acceptedThisSession = true;
            _settings.RestoreWarningEverShown = true;
            if (result.DontShowAgain)
                _settings.DontShowFullRestoreWarning = true;
            _storage.SaveSettings(_settings);
            return true;
        }

        if (!await RestoreDialogs.ShowShortConfirmAsync(root))
            return false;

        _acceptedThisSession = true;
        return true;
    }

    private async Task<bool> RunCreateAsync(XamlRoot root, bool offerContinueWithout, bool manageBusy)
    {
        void Busy(string text)
        {
            if (manageBusy) _beginBusy(text);
            else _setBusyText(text);
        }

        Busy("Creating restore point…");
        ShowOpenSystemRestore = false;
        ShowEnableProtection = false;

        try
        {
            var result = await _restorePoints.CreateAsync();
            if (!result.Success && result.SystemProtectionDisabled)
            {
                if (manageBusy) _endBusy();
                if (await RestoreDialogs.ShowEnableProtectionAsync(root, result.Message))
                {
                    Busy("Enabling System Protection…");
                    var enable = await _restorePoints.EnableSystemProtectionAsync();
                    _log.Info(enable.Message);
                    if (enable.Success)
                    {
                        Busy("Creating restore point…");
                        result = await _restorePoints.CreateAsync();
                    }
                    else
                    {
                        result = enable;
                    }
                }
            }

            if (result.Success)
            {
                try { await CreateJsonBackupAsync(); }
                catch (Exception ex) { _log.Warn($"JSON backup after restore point failed: {ex.Message}"); }

                ShowInfoBar("Restore point created", result.Message, InfoBarSeverity.Success, openSystemRestore: true);
                _setStatus(result.Message, false);
                return true;
            }

            ShowInfoBar("Restore point failed", result.Message, InfoBarSeverity.Warning,
                enableProtection: result.SystemProtectionDisabled);
            _setStatus(result.Message, true);

            if (offerContinueWithout)
                return await RestoreDialogs.ShowContinueWithoutAsync(root, result.Message);

            return false;
        }
        catch (Exception ex)
        {
            _log.Error($"Restore point flow failed: {ex.Message}");
            ShowInfoBar("Restore point failed", ex.Message, InfoBarSeverity.Error);
            _setStatus(ex.Message, true);
            if (offerContinueWithout)
                return await RestoreDialogs.ShowContinueWithoutAsync(root, ex.Message);
            return false;
        }
        finally
        {
            if (manageBusy) _endBusy();
        }
    }

    private async Task CreateJsonBackupAsync()
    {
        var adapter = _getAdapter();
        var entries = new List<BackupValueEntry>();
        foreach (var section in _getSections())
        {
            foreach (var item in section.Items.Where(i => i.Definition.Backend == TweakBackend.Registry))
            {
                try
                {
                    var path = PathTemplates.Resolve(item.Definition.PathTemplate, adapter);
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
            Id = $"rp-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            CreatedUtc = DateTimeOffset.UtcNow,
            Label = "Before restore point",
            AdapterGuid = adapter?.InterfaceGuid.ToString(),
            AdapterDescription = adapter?.InterfaceDescription,
            Entries = entries
        };
        await _backups.CreateSnapshotAsync(snap);
        _log.Info($"JSON backup saved with restore point ({snap.Id}).");
    }

    [RelayCommand]
    private void OpenSystemRestore() => _restorePoints.OpenSystemRestoreUi();

    [RelayCommand]
    private async Task EnableProtectionAsync()
    {
        _beginBusy("Enabling System Protection…");
        try
        {
            var result = await _restorePoints.EnableSystemProtectionAsync();
            ShowInfoBar(result.Success ? "System Protection" : "System Protection failed",
                result.Message,
                result.Success ? InfoBarSeverity.Informational : InfoBarSeverity.Error,
                enableProtection: !result.Success);
            _setStatus(result.Message, !result.Success);
        }
        finally
        {
            _endBusy();
        }
    }

    private void ShowInfoBar(
        string title,
        string message,
        InfoBarSeverity severity,
        bool openSystemRestore = false,
        bool enableProtection = false)
    {
        InfoBarTitle = title;
        InfoBarMessage = message;
        InfoBarSeverity = severity;
        ShowOpenSystemRestore = openSystemRestore;
        ShowEnableProtection = enableProtection;
        InfoBarOpen = true;
    }

    private static XamlRoot? GetXamlRoot() =>
        App.MainAppWindow?.Content?.XamlRoot;
}
