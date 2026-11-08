using System.Diagnostics;
using System.Reflection;
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
    public string AppVersion { get; }

    /// <summary>Placeholder GitHub Releases URL — wire to the real repo when published.</summary>
    public const string ReleasesUrl = "https://github.com/PLACEHOLDER/KwanteksNICTweaker/releases";

    public SettingsViewModel(IStorageService storage, IRestorePointService restorePoints)
    {
        _storage = storage;
        _restorePoints = restorePoints;
        _settings = storage.LoadSettings();
        Theme = _settings.Theme;
        CreateRestorePointBeforeApply = _settings.CreateRestorePointBeforeApply;
        DataPath = storage.DataRoot;
        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
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
        var result = await _restorePoints.CreateAsync();
        StatusMessage = result.Message;
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = DataPath,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ReleasesUrl,
            UseShellExecute = true
        });
    }
}
