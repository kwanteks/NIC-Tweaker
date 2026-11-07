using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public interface IStorageService
{
    string DataRoot { get; }
    bool UsingFallbackLocation { get; }
    string BackupsDirectory { get; }
    string LogsDirectory { get; }
    string SettingsPath { get; }
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
}
