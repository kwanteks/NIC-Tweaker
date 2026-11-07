namespace Kwanteks.NicTweaker.Models;

public enum TweakSection
{
    RssSettings,
    GlobalSettings,
    AdvAdapter,
    RssGlobal,
    PowerSaving,
    InterfaceSettings,
    InterruptSettings,
    AfdTweaks
}

public enum TweakValueType
{
    DWord,
    String,
    Bool,
    Enum
}

public enum TweakBackend
{
    Registry,
    NetAdapterRss,
    NetOffloadGlobal,
    NetIpInterface,
    Action
}

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public enum RestartRequirement
{
    None,
    Adapter,
    Reboot
}

public sealed record TweakOption(string Display, string Value);

public sealed class TweakDefinition
{
    public required string Id { get; init; }
    public required TweakSection Section { get; init; }
    public required string Label { get; init; }
    public string? PathTemplate { get; init; }
    public string? ValueName { get; init; }
    public TweakValueType ValueType { get; init; } = TweakValueType.DWord;
    public TweakBackend Backend { get; init; } = TweakBackend.Registry;
    public IReadOnlyList<TweakOption> Options { get; init; } = Array.Empty<TweakOption>();
    public bool SupportsDelete { get; init; }
    public string DefaultDisplay { get; init; } = "Not set (driver default)";
    public string Description { get; init; } = "";
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;
    public RestartRequirement RequiresRestart { get; init; } = RestartRequirement.None;
    public bool AllowFreeText { get; init; }
    public bool IsReadOnly { get; init; }
    public string? NetIpProperty { get; init; }
    public string? OffloadProperty { get; init; }
    public string? RssProperty { get; init; }
    public int? MinValue { get; init; }
    public int? MaxValue { get; init; }
}
