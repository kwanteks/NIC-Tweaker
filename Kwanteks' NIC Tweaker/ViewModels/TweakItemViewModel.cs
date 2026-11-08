using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Microsoft.UI.Xaml;

namespace Kwanteks.NicTweaker.ViewModels;

public partial class TweakItemViewModel : ObservableObject
{
    public const string NotAvailableDisplay = "N/A for this adapter";

    public TweakDefinition Definition { get; }

    public string Label => Definition.Label;
    public string TooltipText =>
        $"{Definition.Description}\nRisk: {Definition.RiskLevel}" +
        (string.IsNullOrEmpty(RestartText) ? "" : $"\n{RestartText}") +
        (IsNotAvailable ? "\nNot supported on this adapter" : "");

    public string RestartText => Definition.RequiresRestart switch
    {
        RestartRequirement.Adapter => "Adapter restart may be required",
        RestartRequirement.Reboot => "Reboot may be required",
        _ => ""
    };

    public bool AllowFreeText => !IsNotAvailable && (Definition.AllowFreeText || Definition.Options.Count == 0);
    public Visibility UseCombo => Visibility.Visible;
    public double EditorOpacity => IsNotAvailable ? 0.45 : 1.0;
    public bool IsEditorEnabled => !Definition.IsReadOnly && !IsNotAvailable;

    public ObservableCollection<string> OptionDisplays { get; } = new();

    public string? EffectiveDefaultDisplay { get; private set; }

    [ObservableProperty] private string? _selectedDisplay;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private bool _isNotSet;
    [ObservableProperty] private bool _isNotAvailable;

    public TweakItemViewModel(TweakDefinition definition)
    {
        Definition = definition;
        RebuildOptions();
    }

    partial void OnIsNotAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEditorEnabled));
        OnPropertyChanged(nameof(EditorOpacity));
        OnPropertyChanged(nameof(AllowFreeText));
        OnPropertyChanged(nameof(TooltipText));
    }

    public void LoadFrom(TweakCurrentValue value)
    {
        ValidationError = value.ValidationError;
        IsNotSet = value.IsNotSet;
        IsNotAvailable = value.IsNotAvailable
            || string.Equals(value.DisplayValue, NotAvailableDisplay, StringComparison.OrdinalIgnoreCase);

        if (IsNotAvailable)
        {
            OptionDisplays.Clear();
            OptionDisplays.Add(NotAvailableDisplay);
            ForceSelect(NotAvailableDisplay);
            EffectiveDefaultDisplay = null;
            return;
        }

        RebuildOptions();

        // Always prefer a real current value — never show "Not set (default)".
        string? display = null;

        if (!string.IsNullOrWhiteSpace(value.RawValue))
        {
            var byRaw = Definition.Options.FirstOrDefault(o =>
                o.Value.Equals(value.RawValue, StringComparison.OrdinalIgnoreCase));
            display = byRaw?.Display;
            if (string.IsNullOrWhiteSpace(display))
                display = Sanitize(value.DisplayValue) ?? value.RawValue;
        }

        display ??= Sanitize(value.DriverDefaultDisplay);
        display ??= Sanitize(value.DisplayValue);

        if (string.IsNullOrWhiteSpace(display) && Definition.Options.Count > 0)
            display = Definition.Options[0].Display;

        if (string.IsNullOrWhiteSpace(display))
            display = value.RawValue ?? "";

        EffectiveDefaultDisplay = Sanitize(value.DriverDefaultDisplay) ?? display;

        var match = Definition.Options.FirstOrDefault(o =>
            (!string.IsNullOrWhiteSpace(value.RawValue)
                && o.Value.Equals(value.RawValue, StringComparison.OrdinalIgnoreCase))
            || o.Display.Equals(display, StringComparison.OrdinalIgnoreCase));
        ForceSelect(match?.Display ?? display);
    }

    private static string? Sanitize(string? display)
    {
        if (string.IsNullOrWhiteSpace(display)) return null;
        if (display.StartsWith("Not set", StringComparison.OrdinalIgnoreCase)) return null;
        if (display.Equals(NotAvailableDisplay, StringComparison.OrdinalIgnoreCase)) return null;
        return display;
    }

    public void EnsureOptionPublic(string display) => EnsureInList(display);

    public string? DesiredValue => IsNotAvailable ? null : SelectedDisplay;

    public bool Validate(ITweakEngine engine)
    {
        if (IsNotAvailable) { ValidationError = null; return true; }
        ValidationError = engine.Validate(Definition, SelectedDisplay);
        return ValidationError is null;
    }

    private void RebuildOptions()
    {
        OptionDisplays.Clear();
        foreach (var o in Definition.Options)
        {
            if (!OptionDisplays.Contains(o.Display))
                OptionDisplays.Add(o.Display);
        }
    }

    private void EnsureInList(string display)
    {
        if (string.IsNullOrWhiteSpace(display)) return;
        if (display.StartsWith("Not set", StringComparison.OrdinalIgnoreCase)) return;
        if (!OptionDisplays.Contains(display))
            OptionDisplays.Insert(0, display);
    }

    private void ForceSelect(string next)
    {
        if (string.IsNullOrWhiteSpace(next)) return;
        EnsureInList(next);
        if (OptionDisplays.Contains(next))
        {
            OptionDisplays.Remove(next);
            OptionDisplays.Insert(0, next);
        }

        if (string.Equals(SelectedDisplay, next, StringComparison.Ordinal))
            SelectedDisplay = null;
        SelectedDisplay = next;
    }
}
