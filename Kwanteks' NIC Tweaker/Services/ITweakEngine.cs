using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public sealed class TweakCurrentValue
{
    public required string TweakId { get; init; }
    public string? RawValue { get; init; }
    public string DisplayValue { get; init; } = "";
    public bool IsNotSet { get; init; }
    public string? ValidationError { get; set; }
}

public interface ITweakEngine
{
    Task<IReadOnlyDictionary<string, TweakCurrentValue>> ReadSectionAsync(
        TweakSection section,
        AdapterInfo? adapter,
        string? addressFamily = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, TweakCurrentValue>> ReadTweaksAsync(
        IEnumerable<TweakDefinition> definitions,
        AdapterInfo? adapter,
        string? addressFamily = null,
        CancellationToken cancellationToken = default);

    Task<TweakApplyBatchResult> ApplyAsync(
        TweakSection section,
        AdapterInfo? adapter,
        IReadOnlyDictionary<string, string?> desiredValues,
        string? addressFamily = null,
        bool createBackup = true,
        CancellationToken cancellationToken = default);

    Task<TweakApplyBatchResult> ApplyTweaksAsync(
        IEnumerable<TweakDefinition> definitions,
        AdapterInfo? adapter,
        IReadOnlyDictionary<string, string?> desiredValues,
        string? addressFamily = null,
        bool createBackup = true,
        CancellationToken cancellationToken = default);

    string? Validate(TweakDefinition def, string? desiredDisplayOrValue);
}
