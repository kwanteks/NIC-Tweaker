using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public interface IBackupService
{
    Task<BackupSnapshot> CreateSnapshotAsync(BackupSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackupSnapshot>> ListAsync(CancellationToken cancellationToken = default);
    Task<BackupSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task RestoreAsync(BackupSnapshot snapshot, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task EnsureOriginalAsync(Func<Task<BackupSnapshot>> factory, CancellationToken cancellationToken = default);
    Task ExportAsync(string id, string destinationPath, CancellationToken cancellationToken = default);
    Task<BackupSnapshot> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
    Task PruneAsync(int keepCount, CancellationToken cancellationToken = default);
    Task<BackupSnapshot?> GetLastChangeAsync(CancellationToken cancellationToken = default);
}
