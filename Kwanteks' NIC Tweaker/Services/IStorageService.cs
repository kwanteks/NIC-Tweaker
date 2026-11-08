using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public interface IStorageService
{
    string DataRoot { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
    string PresetsDirectory { get; }
    string DriverBackupDirectory { get; }
    string RestorePointsDirectory { get; }
    string ExportsDirectory { get; }
    string SettingsPath { get; }
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
}
