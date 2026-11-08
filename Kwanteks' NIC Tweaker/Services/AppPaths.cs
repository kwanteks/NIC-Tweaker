namespace Kwanteks.NicTweaker.Services;

/// <summary>Single source of truth for installed-app data locations under %ProgramData%\KwanteksNICTweaker.</summary>
public static class AppPaths
{
    public const string ProductFolderName = "KwanteksNICTweaker";

    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolderName);

    public static string RestorePoints => Path.Combine(DataRoot, "RestorePoints");
    public static string Exports => Path.Combine(DataRoot, "Exports");
    public static string Presets => Path.Combine(DataRoot, "Presets");
    public static string DriverBackup => Path.Combine(DataRoot, "DriverBackup");
    public static string Logs => Path.Combine(DataRoot, "Logs");
    public static string Backups => Path.Combine(DataRoot, "Backups");
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string AppLogFile => Path.Combine(Logs, "app.log");
    public static string CrashLogFile => Path.Combine(Logs, "crash.log");

    /// <summary>Creates ProgramData folders. Installer also creates them with Users:Modify; this covers first run / debug builds.</summary>
    public static void EnsureCreated()
    {
        foreach (var dir in new[] { DataRoot, RestorePoints, Exports, Presets, DriverBackup, Logs, Backups })
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// One-time copy from old portable Data\ (next to exe) or LocalAppData fallback into ProgramData.
    /// Never overwrites existing destination files; never deletes the source.
    /// </summary>
    public static void MigrateFromLegacyIfNeeded()
    {
        EnsureCreated();
        var marker = Path.Combine(DataRoot, ".migrated-from-portable");
        if (File.Exists(marker))
            return;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kwanteks' NIC Tweaker"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkTweaker"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetworkTweaker"),
        };

        var any = false;
        foreach (var src in candidates)
        {
            if (!Directory.Exists(src)) continue;
            // Host used LocalAppData\...\app for binaries — skip that tree.
            if (string.Equals(Path.GetFileName(src), "app", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                CopyDirectoryMerge(src, DataRoot);
                any = true;
            }
            catch
            {
                // Migration is best-effort; app must still start.
            }
        }

        try
        {
            File.WriteAllText(marker, any
                ? $"migrated {DateTimeOffset.UtcNow:o}"
                : $"no-legacy-data {DateTimeOffset.UtcNow:o}");
        }
        catch { /* ignore */ }
    }

    /// <summary>Seed ProgramData presets from files shipped next to the exe (read-only install content).</summary>
    public static void SeedBundledPresets()
    {
        EnsureCreated();
        var bundled = Path.Combine(AppContext.BaseDirectory, "Data", "Presets");
        if (!Directory.Exists(bundled)) return;
        foreach (var file in Directory.EnumerateFiles(bundled, "*.json"))
        {
            var dest = Path.Combine(Presets, Path.GetFileName(file));
            if (!File.Exists(dest))
            {
                try { File.Copy(file, dest); }
                catch { /* ignore */ }
            }
        }
    }

    private static void CopyDirectoryMerge(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            if (File.Exists(dest)) continue;
            File.Copy(file, dest);
        }
        foreach (var sub in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(sub);
            if (string.Equals(name, "app", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyDirectoryMerge(sub, Path.Combine(destDir, name));
        }
    }
}
