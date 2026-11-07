using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public interface IConfigTransferService
{
    Task<NetworkTweakerConfig> BuildExportAsync(
        AdapterInfo adapter,
        string? addressFamily,
        bool windowsDefaultsOnly,
        string? label = null,
        CancellationToken cancellationToken = default);

    Task SaveAtomicAsync(NetworkTweakerConfig config, string path, CancellationToken cancellationToken = default);

    Task<NetworkTweakerConfig> LoadAndValidateAsync(string path, CancellationToken cancellationToken = default);

    Task<ConfigImportPreview> BuildPreviewAsync(
        NetworkTweakerConfig config,
        AdapterInfo adapter,
        string? addressFamily,
        CancellationToken cancellationToken = default);

    Task<ConfigApplyResult> ApplyAsync(
        AdapterInfo adapter,
        string? addressFamily,
        IReadOnlyList<ConfigDiffRow> selectedRows,
        CancellationToken cancellationToken = default);
}
