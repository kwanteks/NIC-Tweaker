namespace Kwanteks.NicTweaker.Models;

public static class ConfigFormat
{
    public const string Name = "networktweaker-config";
    public const int SchemaVersion = 1;
    public const long MaxBytes = 2 * 1024 * 1024;

    // Pseudo-ids (not in TweakCatalog) for ancillary UI settings
    public const string IdDisableNetBt = "app.disableNetBt";
    public const string IdDisableNetBios = "app.disableNetBios";
    public const string IdDisableLmhosts = "app.disableLmhosts";
    public const string IdAffinityMask = "app.affinityMask";
}

public sealed class NetworkTweakerConfig
{
    public string Format { get; set; } = ConfigFormat.Name;
    public int SchemaVersion { get; set; } = ConfigFormat.SchemaVersion;
    public string AppVersion { get; set; } = "";
    public DateTimeOffset ExportedUtc { get; set; }
    public string WindowsBuild { get; set; } = "";
    public string? Label { get; set; }
    public ConfigAdapterInfo? Adapter { get; set; }
    public List<ConfigTweakEntry> Tweaks { get; set; } = new();
}

public sealed class ConfigAdapterInfo
{
    public string? Description { get; set; }
    public string? InterfaceGuid { get; set; }
    public string? NetCfgInstanceId { get; set; }
    public string? DriverProvider { get; set; }
    public string? DriverVersion { get; set; }
}

public sealed class ConfigTweakEntry
{
    public required string Id { get; set; }
    public string Section { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Set | NotSet</summary>
    public required string State { get; set; }
    public string? ValueKind { get; set; }
    public string? Value { get; set; }
}

public sealed class ConfigImportPreview
{
    public required NetworkTweakerConfig Config { get; init; }
    public string? AdapterWarning { get; init; }
    public List<ConfigDiffRow> Rows { get; init; } = new();
    public List<string> Skipped { get; init; } = new();
    public int ChangedCount => Rows.Count(r => r.Changed);
    public int UnchangedCount => Rows.Count(r => !r.Changed);
}

public sealed class ConfigDiffRow
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Section { get; init; }
    public required string CurrentDisplay { get; init; }
    public required string ImportedDisplay { get; init; }
    public required string? ImportedDesired { get; init; } // value passed to Apply (display or Not set)
    public bool Changed { get; init; }
    public bool Selected { get; set; } = true;
}

public sealed class ConfigApplyResult
{
    public int Applied { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public bool AdapterRestartRecommended { get; set; }
    public bool RebootRecommended { get; set; }
    public List<string> Details { get; } = new();
    public string Summary => $"{Applied} applied, {Skipped} skipped, {Failed} failed.";
}
