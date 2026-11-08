using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class SectionViewModel : ObservableObject
{
    private readonly ITweakEngine _engine;
    private readonly Func<AdapterInfo?> _getAdapter;
    private readonly Func<string> _getAddressFamily;
    private readonly Func<Task<bool>> _beforeApply;
    private readonly Action<string, bool> _notify;
    private readonly Action<RestartRequirement> _onRestart;
    private readonly IReadOnlyList<TweakDefinition> _defs;

    public string Title { get; }
    public ObservableCollection<TweakItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isBusy;

    public SectionViewModel(
        string title,
        IEnumerable<TweakDefinition> definitions,
        ITweakEngine engine,
        Func<AdapterInfo?> getAdapter,
        Func<string> getAddressFamily,
        Func<Task<bool>> beforeApply,
        Action<string, bool> notify,
        Action<RestartRequirement> onRestart)
    {
        Title = title;
        _defs = definitions.ToList();
        _engine = engine;
        _getAdapter = getAdapter;
        _getAddressFamily = getAddressFamily;
        _beforeApply = beforeApply;
        _notify = notify;
        _onRestart = onRestart;

        foreach (var def in _defs)
            Items.Add(new TweakItemViewModel(def));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var values = await _engine.ReadTweaksAsync(_defs, _getAdapter(), _getAddressFamily(), cancellationToken);
            foreach (var item in Items)
            {
                if (values.TryGetValue(item.Definition.Id, out var v))
                    item.LoadFrom(v);
                else
                    item.LoadFrom(new TweakCurrentValue
                    {
                        TweakId = item.Definition.Id,
                        IsNotSet = true,
                        DisplayValue = item.Definition.Options.FirstOrDefault()?.Display ?? ""
                    });
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var writable = Items.Where(i => i.IsEditorEnabled).ToList();
        if (writable.Any(i => !i.Validate(_engine)))
        {
            _notify("Fix validation errors before applying.", true);
            return;
        }

        IsBusy = true;
        try
        {
            if (!await _beforeApply())
            {
                _notify($"{Title}: cancelled.", false);
                return;
            }
            var desired = writable.ToDictionary(i => i.Definition.Id, i => i.DesiredValue);
            var result = await _engine.ApplyTweaksAsync(_defs, _getAdapter(), desired, _getAddressFamily());
            var failed = result.Items.Where(i => !i.Success && !i.Skipped).ToList();
            if (failed.Count == 0)
                _notify($"{Title}: applied.", false);
            else
                _notify($"{Title}: {failed.Count} failed — {failed[0].Message}", true);

            if (result.AnyRestartRequired)
                _onRestart(result.MaxRestart);

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _notify($"{Title} apply failed: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ApplyPresetValues(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var item in Items)
        {
            if (values.TryGetValue(item.Definition.Id, out var v) && !string.IsNullOrWhiteSpace(v))
                item.SelectedDisplay = v;
        }
    }
}
