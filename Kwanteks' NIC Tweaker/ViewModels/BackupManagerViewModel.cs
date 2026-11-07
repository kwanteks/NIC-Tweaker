using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class BackupManagerViewModel : ObservableObject
{
    private readonly IBackupService _backups;
    private readonly ILoggingService _log;

    public ObservableCollection<BackupSnapshot> Snapshots { get; } = new();

    [ObservableProperty] private BackupSnapshot? _selected;
    [ObservableProperty] private string _diffText = "";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _isBusy;

    public BackupManagerViewModel(IBackupService backups, ILoggingService log)
    {
        _backups = backups;
        _log = log;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Snapshots.Clear();
            foreach (var s in await _backups.ListAsync())
                Snapshots.Add(s);
        }
        finally { IsBusy = false; }
    }

    partial void OnSelectedChanged(BackupSnapshot? value)
    {
        if (value is null)
        {
            DiffText = "";
            return;
        }

        DiffText = string.Join(Environment.NewLine, value.Entries.Select(e =>
            e.DidNotExist
                ? $"{e.TweakId}: (did not exist) → {e.NewValue ?? "(delete)"}"
                : $"{e.TweakId}: {e.PreviousValue} → {(e.Deleted ? "(deleted)" : e.NewValue)}"));
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (Selected is null) return;
        IsBusy = true;
        try
        {
            await _backups.RestoreAsync(Selected);
            StatusMessage = $"Restored snapshot '{Selected.Label}'.";
            StatusIsError = false;
            _log.Info(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RevertLastAsync()
    {
        var last = await _backups.GetLastChangeAsync();
        if (last is null)
        {
            StatusMessage = "No previous change snapshot.";
            StatusIsError = true;
            return;
        }
        Selected = last;
        await RestoreAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null) return;
        try
        {
            await _backups.DeleteAsync(Selected.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            StatusIsError = true;
        }
    }

    [RelayCommand]
    private async Task ExportAsync(string? path)
    {
        if (Selected is null || string.IsNullOrWhiteSpace(path)) return;
        await _backups.ExportAsync(Selected.Id, path);
        StatusMessage = "Exported.";
        StatusIsError = false;
    }

    [RelayCommand]
    private async Task ImportAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        await _backups.ImportAsync(path);
        await LoadAsync();
        StatusMessage = "Imported.";
        StatusIsError = false;
    }
}
