namespace Kwanteks.NicTweaker.Models;

public sealed class BackupValueEntry
{
    public required string TweakId { get; init; }
    public required string Path { get; init; }
    public required string ValueName { get; init; }
    public string? ValueType { get; init; }
    public string? PreviousValue { get; init; }
    public bool DidNotExist { get; init; }
    public string? NewValue { get; init; }
    public bool Deleted { get; init; }
}

public sealed class BackupSnapshot
{
    public required string Id { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string Label { get; set; }
    public bool IsOriginal { get; set; }
    public string? AdapterGuid { get; init; }
    public string? AdapterDescription { get; init; }
    public string? Section { get; init; }
    public List<BackupValueEntry> Entries { get; init; } = new();
}
