using Kwanteks.NicTweaker.Models;
using Kwanteks.NicTweaker.Services;
using Microsoft.Win32;
using Xunit;

namespace Kwanteks.NicTweaker.Tests;

/// <summary>
/// Covers the apply path without touching the real registry: what gets written, what gets backed up,
/// and that invalid input never reaches the registry.
/// </summary>
public class TweakEngineApplyTests
{
    private static (TweakEngine Engine, FakeRegistry Registry, FakeBackup Backup) Create()
    {
        var registry = new FakeRegistry();
        var backup = new FakeBackup();
        // Registry-only definitions never touch the cmdlet-backed services.
        var engine = new TweakEngine(registry, null!, null!, null!, backup, new SilentLog());
        return (engine, registry, backup);
    }

    private static TweakDefinition AfdWindow(string id = "afd.recv") =>
        TweakCatalog.All.First(d => d.Id == id);

    private static Dictionary<string, string?> Desired(TweakDefinition def, string? value) =>
        new(StringComparer.OrdinalIgnoreCase) { [def.Id] = value };

    [Fact]
    public async Task Apply_writes_dword_and_saves_previous_value()
    {
        var (engine, registry, backup) = Create();
        var def = AfdWindow();
        registry.Values[(PathTemplates.AfdParameters, def.ValueName!)] = "8192";

        var result = await engine.ApplyTweaksAsync([def], null, Desired(def, "65536"));

        var write = Assert.Single(registry.Writes);
        Assert.Equal(PathTemplates.AfdParameters, write.Path);
        Assert.Equal(def.ValueName, write.Name);
        Assert.Equal(65536, write.Value);
        Assert.Equal(RegistryValueKind.DWord, write.Kind);

        Assert.True(Assert.Single(result.Items).Success);
        var entry = Assert.Single(backup.Created!.Entries);
        Assert.Equal("8192", entry.PreviousValue);
        Assert.Equal("65536", entry.NewValue);
    }

    [Fact]
    public async Task Invalid_number_is_reported_and_nothing_is_written()
    {
        var (engine, registry, backup) = Create();
        var def = AfdWindow();
        registry.Values[(PathTemplates.AfdParameters, def.ValueName!)] = "8192";

        var result = await engine.ApplyTweaksAsync([def], null, Desired(def, "not-a-number"));

        Assert.Empty(registry.Writes);
        Assert.Null(backup.Created);
        var item = Assert.Single(result.Items);
        Assert.False(item.Success);
        Assert.Contains("number", item.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Out_of_range_value_is_rejected()
    {
        var (engine, registry, _) = Create();
        var def = AfdWindow();
        registry.Values[(PathTemplates.AfdParameters, def.ValueName!)] = "8192";

        var result = await engine.ApplyTweaksAsync([def], null, Desired(def, "-1"));

        Assert.Empty(registry.Writes);
        Assert.False(Assert.Single(result.Items).Success);
    }

    [Fact]
    public async Task Untouched_delete_option_removes_value()
    {
        var (engine, registry, backup) = Create();
        var def = AfdWindow();
        registry.Values[(PathTemplates.AfdParameters, def.ValueName!)] = "8192";

        var result = await engine.ApplyTweaksAsync([def], null, Desired(def, def.DefaultDisplay));

        var delete = Assert.Single(registry.Deletes);
        Assert.Equal(def.ValueName, delete.Name);
        Assert.Empty(registry.Writes);
        Assert.True(Assert.Single(result.Items).Success);
        Assert.True(Assert.Single(backup.Created!.Entries).Deleted);
    }

    [Fact]
    public void Adapter_template_without_adapter_path_throws_instead_of_targeting_the_hive_root()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PathTemplates.Resolve(PathTemplates.InterruptBase + @"\MessageSignaledInterruptProperties", null));
        Assert.Throws<InvalidOperationException>(() =>
            PathTemplates.Resolve(PathTemplates.AdapterClassKey, new AdapterInfo
            {
                InterfaceDescription = "x",
                InterfaceAlias = "x",
                InterfaceGuid = Guid.Empty,
                NetCfgInstanceId = "x"
            }));
    }

    private sealed class FakeRegistry : IRegistryService
    {
        public readonly Dictionary<(string Path, string Name), string> Values = new();
        public readonly List<(string Path, string Name, object Value, RegistryValueKind Kind)> Writes = new();
        public readonly List<(string Path, string Name)> Deletes = new();

        public bool KeyExists(string path) => true;
        public void EnsureKey(string path) { }
        public bool ValueExists(string path, string name) => Values.ContainsKey((path, name));
        public object? GetValue(string path, string name) => Values.TryGetValue((path, name), out var v) ? v : null;
        public string? GetValueAsString(string path, string name) => Values.TryGetValue((path, name), out var v) ? v : null;
        public void SetValue(string path, string name, object value, RegistryValueKind kind) =>
            Writes.Add((path, name, value, kind));
        public void DeleteValue(string path, string name) => Deletes.Add((path, name));
        public IReadOnlyList<string> GetValueNames(string path) => [];
        public IReadOnlyList<string> GetSubKeyNames(string path) => [];
        public string? FindAdapterClassKeyByNetCfgInstanceId(string netCfgInstanceId) => null;
    }

    private sealed class FakeBackup : IBackupService
    {
        public BackupSnapshot? Created { get; private set; }

        public Task<BackupSnapshot> CreateSnapshotAsync(BackupSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Created = snapshot;
            return Task.FromResult(snapshot);
        }

        public Task PruneAsync(int keepCount, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<BackupSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<BackupSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RestoreAsync(BackupSnapshot snapshot, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task EnsureOriginalAsync(Func<Task<BackupSnapshot>> factory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ExportAsync(string id, string destinationPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<BackupSnapshot> ImportAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<BackupSnapshot?> GetLastChangeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SilentLog : ILoggingService
    {
        public event EventHandler<string>? EntryAdded { add { } remove { } }
        public IReadOnlyList<string> Entries => [];
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Clear() { }
    }
}
