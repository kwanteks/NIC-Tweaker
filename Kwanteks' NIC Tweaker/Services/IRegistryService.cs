using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public interface IRegistryService
{
    bool KeyExists(string path);
    void EnsureKey(string path);
    bool ValueExists(string path, string name);
    object? GetValue(string path, string name);
    string? GetValueAsString(string path, string name);
    void SetValue(string path, string name, object value, RegistryValueKind kind);
    void DeleteValue(string path, string name);
    IReadOnlyList<string> GetValueNames(string path);
    IReadOnlyList<string> GetSubKeyNames(string path);
    string? FindAdapterClassKeyByNetCfgInstanceId(string netCfgInstanceId);
}
