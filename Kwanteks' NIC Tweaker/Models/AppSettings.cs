namespace Kwanteks.NicTweaker.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public bool CreateRestorePointBeforeApply { get; set; } = true;
    public bool FirstLaunchWarningShown { get; set; }
    public bool RestoreWarningEverShown { get; set; }
    public bool DontShowFullRestoreWarning { get; set; }
    public bool OpacityReduced { get; set; }
    public int BackupRetentionCount { get; set; } = 20;
}
