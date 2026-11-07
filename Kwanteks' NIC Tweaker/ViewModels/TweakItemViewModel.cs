using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Microsoft.UI.Xaml;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class TweakItemViewModel : ObservableObject
{
    public TweakDefinition Definition { get; }

    public string Label => Definition.Label;
    public string TooltipText =>
        $"{Definition.Description}\nRisk: {Definition.RiskLevel}" +
        (string.IsNullOrEmpty(RestartText) ? "" : $"\n{RestartText}");

    public string RestartText => Definition.RequiresRestart switch
    {
        RestartRequirement.Adapter => "Adapter restart may be required",
        RestartRequirement.Reboot => "Reboot may be required",
        _ => ""
    };

    public bool AllowFreeText => Definition.AllowFreeText || Definition.Options.Count == 0 || IsNotSet;
    public Visibility UseCombo => Visibility.Visible;
    public double EditorOpacity => IsNotAvailable ? 0.45 : 1.0;
    public bool IsEditorEnabled => !Definition.IsReadOnly && !IsNotAvailable;

    public ObservableCollection<string> OptionDisplays { get; } = new();

    [ObservableProperty] private string? _selectedDisplay;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private bool _isNotSet;
    [ObservableProperty] private bool _isNotAvailable;

    public TweakItemViewModel(TweakDefinition definition)
    {
        Definition = definition;
        RebuildOptions(includeNotSet: definition.SupportsDelete);
    }

    partial void OnIsNotAvailableChanged(bool value) => OnPropertyChanged(nameof(IsEditorEnabled));
    partial void OnIsNotSetChanged(bool value) => OnPropertyChanged(nameof(AllowFreeText));

    public void LoadFrom(TweakCurrentValue value)
    {
        ValidationError = value.ValidationError;
        IsNotAvailable = string.Equals(value.DisplayValue, "N/A for this adapter", StringComparison.OrdinalIgnoreCase)
                         || (value.ValidationError is not null && value.IsNotSet && string.IsNullOrWhiteSpace(value.RawValue)
                             && Definition.Backend == TweakBackend.Registry && value.ValidationError.Contains("not resolved", StringComparison.OrdinalIgnoreCase));

        if (IsNotAvailable)
        {
            EnsureOption("N/A for this adapter");
            if (!string.Equals(SelectedDisplay, "N/A for this adapter", StringComparison.Ordinal))
                SelectedDisplay = "N/A for this adapter";
            IsNotSet = true;
            return;
        }

        IsNotSet = value.IsNotSet;
        var display = string.IsNullOrWhiteSpace(value.DisplayValue)
            ? Definition.DefaultDisplay
            : value.DisplayValue;

        if (IsNotSet || display.StartsWith("Not set", StringComparison.OrdinalIgnoreCase))
        {
            display = Definition.DefaultDisplay;
            EnsureOption(Definition.DefaultDisplay);
        }

        EnsureOption(display);
        // Match against known option displays when raw equals a value
        var match = Definition.Options.FirstOrDefault(o =>
            o.Value.Equals(value.RawValue, StringComparison.OrdinalIgnoreCase) ||
            o.Display.Equals(display, StringComparison.OrdinalIgnoreCase));
        var next = match?.Display ?? display;
        // Skip no-op assigns — ComboBox TwoWay re-entry during adapter refresh.
        if (!string.Equals(SelectedDisplay, next, StringComparison.Ordinal))
            SelectedDisplay = next;
    }

    public void EnsureOptionPublic(string display) => EnsureOption(display);

    public string? DesiredValue => SelectedDisplay;

    public bool Validate(ITweakEngine engine)
    {
        if (IsNotAvailable) { ValidationError = null; return true; }
        ValidationError = engine.Validate(Definition, SelectedDisplay);
        return ValidationError is null;
    }

    private void RebuildOptions(bool includeNotSet)
    {
        OptionDisplays.Clear();
        if (includeNotSet)
            OptionDisplays.Add(Definition.DefaultDisplay);
        foreach (var o in Definition.Options)
        {
            if (!OptionDisplays.Contains(o.Display))
                OptionDisplays.Add(o.Display);
        }
        if (OptionDisplays.Count == 0)
            OptionDisplays.Add(Definition.DefaultDisplay);
    }

    private void EnsureOption(string display)
    {
        if (!OptionDisplays.Contains(display))
            OptionDisplays.Insert(0, display);
    }
}
