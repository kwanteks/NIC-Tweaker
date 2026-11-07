namespace Kwanteks.NicTweaker.Services;

public sealed class RestorePointResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public bool IsWarning { get; init; }
    public bool SystemProtectionDisabled { get; init; }
    public string? Description { get; init; }
    public DateTime? CreatedLocal { get; init; }
}

public sealed class DriverExportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? FolderPath { get; init; }
}

public interface IRestorePointService
{
    Task<RestorePointResult> CreateAsync(string? description = null, CancellationToken cancellationToken = default);
    Task<RestorePointResult> EnableSystemProtectionAsync(CancellationToken cancellationToken = default);
    Task<DriverExportResult> ExportAdapterDriverAsync(string? pnpDeviceId, string adapterName, CancellationToken cancellationToken = default);
    void OpenSystemRestoreUi();
    void OpenDeviceManager();
}
