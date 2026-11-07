using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public sealed class RegistryService : IRegistryService
{
    public const string NetClassGuid = @"{4D36E972-E325-11CE-BFC1-08002bE10318}";
    public const string NetClassRoot =
        @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002bE10318}";

    public bool KeyExists(string path)
    {
        using var key = Open(path, writable: false);
        return key is not null;
    }

    public void EnsureKey(string path)
    {
        var (hive, relative) = Split(path);
        using var created = hive.CreateSubKey(relative, writable: true)
            ?? throw new InvalidOperationException($"Could not create registry key: {path}");
    }

    public bool ValueExists(string path, string name)
    {
        using var key = Open(path, writable: false);
        if (key is null) return false;
        return key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public object? GetValue(string path, string name)
    {
        using var key = Open(path, writable: false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public string? GetValueAsString(string path, string name)
    {
        var value = GetValue(path, name);
        return value switch
        {
            null => null,
            string s => s,
            int i => i.ToString(),
            long l => l.ToString(),
            byte[] b when b.Length == 4 => BitConverter.ToInt32(b, 0).ToString(),
            _ => Convert.ToString(value)
        };
    }

    public void SetValue(string path, string name, object value, RegistryValueKind kind)
    {
        EnsureKey(path);
        using var key = Open(path, writable: true)
            ?? throw new InvalidOperationException($"Cannot open registry key for write: {path}");
        key.SetValue(name, value, kind);
    }

    public void DeleteValue(string path, string name)
    {
        using var key = Open(path, writable: true);
        if (key is null) return;
        if (key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            key.DeleteValue(name, throwOnMissingValue: false);
    }

    public IReadOnlyList<string> GetValueNames(string path)
    {
        using var key = Open(path, writable: false);
        return key?.GetValueNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetSubKeyNames(string path)
    {
        using var key = Open(path, writable: false);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public string? FindAdapterClassKeyByNetCfgInstanceId(string netCfgInstanceId)
    {
        using var root = Registry.LocalMachine.OpenSubKey(NetClassRoot, writable: false);
        if (root is null) return null;

        foreach (var subName in root.GetSubKeyNames())
        {
            if (subName.Equals("Properties", StringComparison.OrdinalIgnoreCase))
                continue;

            using var sub = root.OpenSubKey(subName, writable: false);
            if (sub is null) continue;

            var instanceId = sub.GetValue("NetCfgInstanceId") as string;
            if (string.Equals(instanceId, netCfgInstanceId, StringComparison.OrdinalIgnoreCase))
                return $@"HKLM\{NetClassRoot}\{subName}";
        }

        return null;
    }

    private static RegistryKey? Open(string path, bool writable)
    {
        var (hive, relative) = Split(path);
        return hive.OpenSubKey(relative, writable);
    }

    private static (RegistryKey hive, string relative) Split(string path)
    {
        var normalized = path
            .Replace("REGISTRY::", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Computer\\", "", StringComparison.OrdinalIgnoreCase)
            .Trim('\\');

        if (normalized.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
        {
            var idx = normalized.IndexOf('\\');
            return (Registry.LocalMachine, normalized[(idx + 1)..]);
        }

        if (normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
        {
            var idx = normalized.IndexOf('\\');
            return (Registry.CurrentUser, normalized[(idx + 1)..]);
        }

        return (Registry.LocalMachine, normalized);
    }
}
