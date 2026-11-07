namespace Kwanteks.NicTweaker.Models;

public sealed class TweakApplyItemResult
{
    public required string TweakId { get; init; }
    public required string Label { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
    public bool Skipped { get; init; }
    public RestartRequirement RestartRequirement { get; init; }
}

public sealed class TweakApplyBatchResult
{
    public List<TweakApplyItemResult> Items { get; } = new();
    public BackupSnapshot? Snapshot { get; set; }
    public bool AnyRestartRequired => Items.Any(i => i.Success && i.RestartRequirement != RestartRequirement.None);
    public RestartRequirement MaxRestart =>
        Items.Where(i => i.Success).Select(i => i.RestartRequirement).DefaultIfEmpty(RestartRequirement.None).Max();
    public bool AllSucceeded => Items.All(i => i.Success || i.Skipped);
}
