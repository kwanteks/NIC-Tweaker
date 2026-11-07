using System.Text.Json;
using Kwanteks.NicTweaker.Models;
using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IStorageService _storage;
    private readonly IRegistryService _registry;
    private readonly ILoggingService _log;

    public BackupService(IStorageService storage, IRegistryService registry, ILoggingService log)
    {
        _storage = storage;
        _registry = registry;
        _log = log;
    }

    public Task<BackupSnapshot> CreateSnapshotAsync(BackupSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var file = Path.Combine(_storage.BackupsDirectory, $"{snapshot.Id}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(snapshot, JsonOptions));
            _log.Info($"Backup saved: {snapshot.Label} ({snapshot.Id})");
            return snapshot;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<BackupSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var list = new List<BackupSnapshot>();
            foreach (var file in Directory.EnumerateFiles(_storage.BackupsDirectory, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var snap = JsonSerializer.Deserialize<BackupSnapshot>(File.ReadAllText(file), JsonOptions);
                    if (snap is not null) list.Add(snap);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Skipping corrupt backup {file}: {ex.Message}");
                }
            }
            return (IReadOnlyList<BackupSnapshot>)list
                .OrderByDescending(s => s.IsOriginal)
                .ThenByDescending(s => s.CreatedUtc)
                .ToList();
        }, cancellationToken);
    }

    public async Task<BackupSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(_storage.BackupsDirectory, $"{id}.json");
        if (!File.Exists(file)) return null;
        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<BackupSnapshot>(stream, JsonOptions, cancellationToken);
    }

    public Task RestoreAsync(BackupSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            foreach (var entry in snapshot.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (entry.DidNotExist || entry.PreviousValue is null && entry.DidNotExist)
                    {
                        _registry.DeleteValue(entry.Path, entry.ValueName);
                        _log.Info($"Restored (deleted): {entry.Path}\\{entry.ValueName}");
                        continue;
                    }

                    if (entry.DidNotExist)
                    {
                        _registry.DeleteValue(entry.Path, entry.ValueName);
                        continue;
                    }

                    var kind = ParseKind(entry.ValueType);
                    object value = kind == RegistryValueKind.DWord
                        ? (object)(int.TryParse(entry.PreviousValue, out var i) ? i : 0)
                        : entry.PreviousValue ?? "";
                    _registry.SetValue(entry.Path, entry.ValueName, value, kind);
                    _log.Info($"Restored: {entry.Path}\\{entry.ValueName} = {entry.PreviousValue}");
                }
                catch (Exception ex)
                {
                    _log.Error($"Restore failed for {entry.TweakId}: {ex.Message}");
                    throw;
                }
            }
        }, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var snap = GetAsync(id, cancellationToken).GetAwaiter().GetResult();
            if (snap?.IsOriginal == true)
                throw new InvalidOperationException("Original state snapshot cannot be deleted.");
            var file = Path.Combine(_storage.BackupsDirectory, $"{id}.json");
            if (File.Exists(file)) File.Delete(file);
        }, cancellationToken);
    }

    public async Task EnsureOriginalAsync(Func<Task<BackupSnapshot>> factory, CancellationToken cancellationToken = default)
    {
        var existing = await ListAsync(cancellationToken);
        if (existing.Any(s => s.IsOriginal)) return;
        var original = await factory();
        original.IsOriginal = true;
        original.Label = "Original state";
        await CreateSnapshotAsync(original, cancellationToken);
    }

    public async Task ExportAsync(string id, string destinationPath, CancellationToken cancellationToken = default)
    {
        var src = Path.Combine(_storage.BackupsDirectory, $"{id}.json");
        if (!File.Exists(src)) throw new FileNotFoundException("Backup not found.", src);
        await Task.Run(() => File.Copy(src, destinationPath, overwrite: true), cancellationToken);
    }

    public async Task<BackupSnapshot> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(sourcePath);
        var snap = await JsonSerializer.DeserializeAsync<BackupSnapshot>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Invalid backup file.");
        snap = new BackupSnapshot
        {
            Id = $"import-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..40],
            CreatedUtc = DateTimeOffset.UtcNow,
            Label = snap.Label + " (imported)",
            IsOriginal = false,
            AdapterGuid = snap.AdapterGuid,
            AdapterDescription = snap.AdapterDescription,
            Section = snap.Section,
            Entries = snap.Entries
        };
        await CreateSnapshotAsync(snap, cancellationToken);
        return snap;
    }

    public async Task PruneAsync(int keepCount, CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(cancellationToken);
        var deletable = all.Where(s => !s.IsOriginal).OrderByDescending(s => s.CreatedUtc).Skip(keepCount).ToList();
        foreach (var snap in deletable)
            await DeleteAsync(snap.Id, cancellationToken);
    }

    public async Task<BackupSnapshot?> GetLastChangeAsync(CancellationToken cancellationToken = default)
    {
        var all = await ListAsync(cancellationToken);
        return all.FirstOrDefault(s => !s.IsOriginal);
    }

    private static RegistryValueKind ParseKind(string? type) =>
        string.Equals(type, "String", StringComparison.OrdinalIgnoreCase)
            ? RegistryValueKind.String
            : RegistryValueKind.DWord;
}
