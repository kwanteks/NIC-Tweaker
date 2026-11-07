using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly IRestorePointService _restorePoints;
    private readonly AppSettings _settings;

    [ObservableProperty] private string _theme;
    [ObservableProperty] private bool _createRestorePointBeforeApply;
    [ObservableProperty] private string? _statusMessage;

    public string DataPath { get; }
    public bool UsingFallback { get; }

    public SettingsViewModel(IStorageService storage, IRestorePointService restorePoints)
    {
        _storage = storage;
        _restorePoints = restorePoints;
        _settings = storage.LoadSettings();
        Theme = _settings.Theme;
        CreateRestorePointBeforeApply = _settings.CreateRestorePointBeforeApply;
        DataPath = storage.DataRoot;
        UsingFallback = storage.UsingFallbackLocation;
    }

    partial void OnThemeChanged(string value)
    {
        _settings.Theme = value;
        _storage.SaveSettings(_settings);
        App.ApplyTheme(value);
    }

    partial void OnCreateRestorePointBeforeApplyChanged(bool value)
    {
        _settings.CreateRestorePointBeforeApply = value;
        _storage.SaveSettings(_settings);
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        var result = await _restorePoints.CreateAsync("Kwanteks' NIC Tweaker (manual)");
        StatusMessage = result.Message;
    }
}
