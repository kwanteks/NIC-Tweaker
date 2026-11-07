namespace Kwanteks.NicTweaker.Services;

public sealed class RestorePointResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public bool IsWarning { get; init; }
}

public interface IRestorePointService
{
    Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default);
}
