using System.Text.Json;
using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public sealed class StorageService : IStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DataRoot => AppPaths.DataRoot;
    public string BackupsDirectory => AppPaths.Backups;
    public string LogsDirectory => AppPaths.Logs;
    public string PresetsDirectory => AppPaths.Presets;
    public string DriverBackupDirectory => AppPaths.DriverBackup;
    public string RestorePointsDirectory => AppPaths.RestorePoints;
    public string ExportsDirectory => AppPaths.Exports;
    public string SettingsPath => AppPaths.SettingsFile;

    public StorageService()
    {
        AppPaths.MigrateFromLegacyIfNeeded();
        AppPaths.EnsureCreated();
        AppPaths.SeedBundledPresets();
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
