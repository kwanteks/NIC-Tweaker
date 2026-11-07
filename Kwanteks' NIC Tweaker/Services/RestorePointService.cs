using System.Diagnostics;
using System.Management;
using System.Text;
using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public sealed class RestorePointService : IRestorePointService
{
    private const string SystemRestoreKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string FrequencyValueName = "SystemRestorePointCreationFrequency";
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromSeconds(90);

    private readonly ILoggingService _log;
    private readonly IStorageService _storage;
    private readonly IPowerShellBridge _ps;

    public RestorePointService(ILoggingService log, IStorageService storage, IPowerShellBridge ps)
    {
        _log = log;
        _storage = storage;
        _ps = ps;
    }

    public Task<RestorePointResult> CreateAsync(string? description = null, CancellationToken cancellationToken = default)
    {
        var desc = description ?? $"Kwanteks NIC Tweaker - {DateTime.Now:yyyy-MM-dd HH:mm}";
        return Task.Run(() => CreateCore(desc, cancellationToken), CancellationToken.None);
    }

    private RestorePointResult CreateCore(string description, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(CreateTimeout);
        var token = timeoutCts.Token;

        object? previousFrequency = null;
        var hadFrequency = false;
        RegistryKey? key = null;

        try
        {
            _log.Info($"Creating restore point: {description}");
            token.ThrowIfCancellationRequested();

            try
            {
                key = Registry.LocalMachine.CreateSubKey(SystemRestoreKey);
                if (key is not null)
                {
                    previousFrequency = key.GetValue(FrequencyValueName);
                    hadFrequency = previousFrequency is not null;
                    key.SetValue(FrequencyValueName, 0, RegistryValueKind.DWord);
                    _log.Info("Set SystemRestorePointCreationFrequency=0 for this create.");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not set SystemRestorePointCreationFrequency: {ex.Message}");
            }

            token.ThrowIfCancellationRequested();

            using var sysRestore = new ManagementClass(@"\\.\root\default:SystemRestore");
            var inParams = sysRestore.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
            inParams["EventType"] = 100; // BEGIN_SYSTEM_CHANGE
            var outParams = sysRestore.InvokeMethod("CreateRestorePoint", inParams, null);
            var status = Convert.ToInt32(outParams?["ReturnValue"] ?? -1);

            token.ThrowIfCancellationRequested();

            if (status != 0)
            {
                var msg = status switch
                {
                    1058 => "System Protection / Volume Shadow Copy service is unavailable (error 1058).",
                    -2147023838 => "A required service is disabled (System Restore or Volume Shadow Copy).",
                    _ => $"CreateRestorePoint returned {status}."
                };
                _log.Warn(msg);
                return new RestorePointResult
                {
                    Success = false,
                    IsWarning = true,
                    SystemProtectionDisabled = status == 1058 || !CanQuerySystemRestore(),
                    Message = msg
                };
            }

            if (!VerifyExists(description, DateTime.Now.AddMinutes(-5)))
            {
                const string msg = "Windows reported success but the restore point was not found (possible 24h skip or System Protection issue).";
                _log.Warn(msg);
                return new RestorePointResult
                {
                    Success = false,
                    IsWarning = true,
                    Message = msg,
                    Description = description
                };
            }

            _log.Info($"Restore point verified: {description}");
            return new RestorePointResult
            {
                Success = true,
                Message = $"Restore point created: {description} ({DateTime.Now:HH:mm}).",
                Description = description,
                CreatedLocal = DateTime.Now
            };
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Restore point create timed out or was cancelled.");
            return new RestorePointResult
            {
                Success = false,
                IsWarning = true,
                Message = "Restore point creation timed out (~90s). Check System Protection and VSS, then retry."
            };
        }
        catch (ManagementException ex)
        {
            _log.Warn($"Restore point WMI failed: {ex.Message}");
            var disabled = ex.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                           || !CanQuerySystemRestore();
            return new RestorePointResult
            {
                Success = false,
                IsWarning = true,
                SystemProtectionDisabled = disabled,
                Message = $"Could not create restore point: {ex.Message}"
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Error($"Restore point access denied: {ex.Message}");
            return new RestorePointResult
            {
                Success = false,
                IsWarning = true,
                Message = $"Access denied creating restore point: {ex.Message}. Run as Administrator."
            };
        }
        catch (Exception ex)
        {
            _log.Warn($"Restore point failed: {ex.Message}");
            return new RestorePointResult
            {
                Success = false,
                IsWarning = true,
                SystemProtectionDisabled = !CanQuerySystemRestore(),
                Message = $"Could not create restore point: {ex.Message}"
            };
        }
        finally
        {
            try
            {
                key ??= Registry.LocalMachine.CreateSubKey(SystemRestoreKey);
                if (key is not null)
                {
                    if (hadFrequency && previousFrequency is not null)
                        key.SetValue(FrequencyValueName, previousFrequency, RegistryValueKind.DWord);
                    else
                        key.DeleteValue(FrequencyValueName, throwOnMissingValue: false);
                    _log.Info("Restored SystemRestorePointCreationFrequency.");
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not restore SystemRestorePointCreationFrequency: {ex.Message}");
            }
            finally
            {
                key?.Dispose();
            }
        }
    }

    public async Task<RestorePointResult> EnableSystemProtectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            if (!drive.EndsWith('\\')) drive += "\\";
            _log.Info($"Enabling System Protection on {drive}");
            await _ps.RunAsync($"Enable-ComputerRestore -Drive '{drive}'", cancellationToken);
            return new RestorePointResult
            {
                Success = true,
                Message = $"System Protection enable requested for {drive}. Retry creating the restore point."
            };
        }
        catch (Exception ex)
        {
            _log.Error($"Enable System Protection failed: {ex.Message}");
            return new RestorePointResult
            {
                Success = false,
                IsWarning = true,
                SystemProtectionDisabled = true,
                Message = $"Could not enable System Protection: {ex.Message}"
            };
        }
    }

    public Task<DriverExportResult> ExportAdapterDriverAsync(
        string? pnpDeviceId,
        string adapterName,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pnpDeviceId))
                    return new DriverExportResult { Success = false, Message = "No PNP device ID for the selected adapter." };

                var inf = FindOemInf(pnpDeviceId);
                if (string.IsNullOrWhiteSpace(inf))
                    return new DriverExportResult { Success = false, Message = "Could not find the OEM INF for this adapter." };

                var safeName = SanitizeFolderName(adapterName);
                var folder = Path.Combine(
                    _storage.DataRoot,
                    "DriverBackup",
                    $"{safeName}_{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.CreateDirectory(folder);

                _log.Info($"Exporting driver {inf} → {folder}");
                var psi = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = $"/export-driver \"{inf}\" \"{folder}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start pnputil.");
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(60_000);

                if (proc.ExitCode != 0)
                {
                    var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    _log.Warn($"pnputil failed ({proc.ExitCode}): {err}");
                    return new DriverExportResult
                    {
                        Success = false,
                        Message = $"Driver export failed: {err.Trim()}"
                    };
                }

                _log.Info($"Driver exported to {folder}");
                return new DriverExportResult
                {
                    Success = true,
                    FolderPath = folder,
                    Message = $"Driver exported to:\n{folder}"
                };
            }
            catch (Exception ex)
            {
                _log.Warn($"Driver export failed: {ex.Message}");
                return new DriverExportResult { Success = false, Message = $"Driver export failed: {ex.Message}" };
            }
        }, cancellationToken);
    }

    public void OpenSystemRestoreUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not open System Restore UI: {ex.Message}");
        }
    }

    public void OpenDeviceManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not open Device Manager: {ex.Message}");
        }
    }

    private static bool CanQuerySystemRestore()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber FROM SystemRestore");
            _ = searcher.Get();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyExists(string description, DateTime notBeforeLocal)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT * FROM SystemRestore");
            foreach (ManagementObject mo in searcher.Get())
            {
                var desc = mo["Description"]?.ToString() ?? "";
                if (!string.Equals(desc, description, StringComparison.Ordinal))
                    continue;

                var created = ParseCimDateTime(mo["CreationTime"]?.ToString());
                if (created is null || created >= notBeforeLocal.AddMinutes(-1))
                    return true;
            }
        }
        catch
        {
            // Fall through
        }
        return false;
    }

    private static DateTime? ParseCimDateTime(string? cim)
    {
        if (string.IsNullOrWhiteSpace(cim) || cim.Length < 14) return null;
        try
        {
            return ManagementDateTimeConverter.ToDateTime(cim);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindOemInf(string pnpDeviceId)
    {
        string? Fuzzy(ManagementObjectCollection rows, bool oemOnly)
        {
            foreach (ManagementObject mo in rows)
            {
                var id = mo["DeviceID"]?.ToString();
                if (id is null) continue;
                var exact = id.Equals(pnpDeviceId, StringComparison.OrdinalIgnoreCase);
                var fuzzy = id.Contains(pnpDeviceId, StringComparison.OrdinalIgnoreCase)
                            || pnpDeviceId.Contains(id, StringComparison.OrdinalIgnoreCase);
                if (!exact && !fuzzy) continue;
                var inf = mo["InfName"]?.ToString();
                if (string.IsNullOrWhiteSpace(inf)) continue;
                if (oemOnly && !inf.StartsWith("oem", StringComparison.OrdinalIgnoreCase)) continue;
                if (exact || oemOnly) return inf;
            }
            return null;
        }

        using (var searcher = new ManagementObjectSearcher("SELECT InfName, DeviceID FROM Win32_PnPSignedDriver"))
        {
            var rows = searcher.Get();
            return Fuzzy(rows, oemOnly: false) ?? Fuzzy(rows, oemOnly: true);
        }
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        var s = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? "adapter" : s;
    }
}
