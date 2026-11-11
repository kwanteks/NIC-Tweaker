using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Xunit;

namespace Kwanteks.NicTweaker.Tests;

public class TweakEngineLogicTests
{
    [Fact]
    public void Empty_input_is_delete_when_SupportsDelete()
    {
        var def = new TweakDefinition
        {
            Id = "t",
            Section = TweakSection.AfdTweaks,
            Label = "Test",
            SupportsDelete = true,
            ValueType = TweakValueType.DWord
        };

        Assert.True(TweakEngine.IsDeleteRequest(def, null));
        Assert.True(TweakEngine.IsDeleteRequest(def, ""));
        Assert.True(TweakEngine.IsDeleteRequest(def, "   "));
        Assert.True(TweakEngine.IsDeleteRequest(def, "Not set (driver default)"));
    }

    [Fact]
    public void NonEmpty_input_is_not_delete()
    {
        var def = new TweakDefinition
        {
            Id = "t",
            Section = TweakSection.AfdTweaks,
            Label = "Test",
            SupportsDelete = true,
            ValueType = TweakValueType.DWord,
            Options = [new("0", "0"), new("1", "1")]
        };

        // Precedence bug in script: $A -eq $true -and $x -eq $null -or $x -eq ''
        // With value present and text "1", must NOT delete.
        Assert.False(TweakEngine.IsDeleteRequest(def, "1"));
        Assert.False(TweakEngine.IsDeleteRequest(def, "0"));
    }

    [Fact]
    public void Delete_sentinel_option_triggers_delete()
    {
        var def = new TweakDefinition
        {
            Id = "t",
            Section = TweakSection.GlobalSettings,
            Label = "NetworkDirect",
            SupportsDelete = true,
            Options = [new("Enabled", "delete"), new("Disabled", "1")]
        };

        Assert.True(TweakEngine.IsDeleteRequest(def, "Enabled"));
        Assert.False(TweakEngine.IsDeleteRequest(def, "Disabled"));
        Assert.Equal("1", TweakEngine.ResolveStoredValue(def, "Disabled"));
        Assert.Equal("delete", TweakEngine.ResolveStoredValue(def, "Enabled"));
    }

    [Fact]
    public void Offload_options_roundtrip_enabled_disabled_without_inventing_first()
    {
        var def = TweakCatalog.All.First(t => t.Id == "global.chimney");
        Assert.Equal("Disabled", TweakEngine.ResolveStoredValue(def, "Disabled"));
        Assert.Equal("Enabled", TweakEngine.ResolveStoredValue(def, "Enabled"));
        Assert.Equal("Disabled", TweakEngine.ResolveStoredValue(def, "disabled"));
        // MapToDisplay path: raw value must stay itself when it matches an option value.
        Assert.Contains(def.Options, o => o.Display.Equals("Disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveStoredValue_maps_display_to_value()
    {
        var def = new TweakDefinition
        {
            Id = "t",
            Section = TweakSection.AdvAdapter,
            Label = "ITR",
            Options =
            [
                new("200 - Minimal", "200"),
                new("65535 - Adaptive", "65535")
            ]
        };

        Assert.Equal("200", TweakEngine.ResolveStoredValue(def, "200 - Minimal"));
        Assert.Equal("65535", TweakEngine.ResolveStoredValue(def, "65535 - Adaptive"));
        Assert.Equal("200", TweakEngine.ResolveStoredValue(def, "200"));
    }

    [Fact]
    public void Catalog_has_expected_sections_and_unique_ids()
    {
        var all = TweakCatalog.All;
        Assert.NotEmpty(all);
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        Assert.Contains(all, t => t.Section == TweakSection.RssSettings);
        Assert.Contains(all, t => t.Section == TweakSection.AfdTweaks);
        Assert.Contains(all, t => t.Id == "int.policy");
        Assert.Contains(all, t => t.Label == "SpreadMessagesAcrossAllProcessors" ||
                                  t.Options.Any(o => o.Display == "SpreadMessagesAcrossAllProcessors"));
    }

    [Fact]
    public void UiGroups_do_not_duplicate_tweak_ids()
    {
        var groups = new IReadOnlyList<TweakDefinition>[]
        {
            UiGroups.RssSettings, UiGroups.RssGlobal, UiGroups.Interrupt, UiGroups.PowerSaving,
            UiGroups.GlobalSettings, UiGroups.InterfaceSettings, UiGroups.AdapterFeatures,
            UiGroups.BuffersCoalesce, UiGroups.OffloadSettings, UiGroups.AfdTweaks
        };
        var ids = groups.SelectMany(g => g.Select(t => t.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(UiGroups.PowerSaving, t => t.Id == "pow.pmarp");
        Assert.DoesNotContain(UiGroups.OffloadSettings, t => t.Id.StartsWith("pow.pm", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(UiGroups.OffloadSettings, t => t.Id is "adv.pmarp" or "adv.pmns" or "adv.pmwifi");
    }

    [Fact]
    public void BackupSnapshot_roundtrips_json()
    {
        var snap = new BackupSnapshot
        {
            Id = "test-1",
            CreatedUtc = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            Label = "test",
            IsOriginal = true,
            Entries =
            [
                new BackupValueEntry
                {
                    TweakId = "afd.recv",
                    Path = @"HKLM\SYSTEM\CurrentControlSet\Services\AFD\Parameters",
                    ValueName = "DefaultReceiveWindow",
                    ValueType = "DWord",
                    PreviousValue = null,
                    DidNotExist = true,
                    NewValue = "8192"
                }
            ]
        };

        var json = System.Text.Json.JsonSerializer.Serialize(snap);
        var back = System.Text.Json.JsonSerializer.Deserialize<BackupSnapshot>(json);
        Assert.NotNull(back);
        Assert.True(back!.IsOriginal);
        Assert.Single(back.Entries);
        Assert.True(back.Entries[0].DidNotExist);
    }
}
