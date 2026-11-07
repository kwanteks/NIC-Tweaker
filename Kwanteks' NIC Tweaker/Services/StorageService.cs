using System.Text.Json;
using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public sealed class StorageService : IStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DataRoot { get; }
    public bool UsingFallbackLocation { get; }
    public string BackupsDirectory => Path.Combine(DataRoot, "Backups");
    public string LogsDirectory => Path.Combine(DataRoot, "Logs");
    public string SettingsPath => Path.Combine(DataRoot, "settings.json");

    public StorageService()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "Data");
        try
        {
            Directory.CreateDirectory(portable);
            var probe = Path.Combine(portable, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            DataRoot = portable;
            UsingFallbackLocation = false;
        }
        catch
        {
            DataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Kwanteks' NIC Tweaker");
            Directory.CreateDirectory(DataRoot);
            UsingFallbackLocation = true;
        }

        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
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
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
