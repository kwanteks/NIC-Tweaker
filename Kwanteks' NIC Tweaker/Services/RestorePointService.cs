using System.Management;
using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public sealed class RestorePointService : IRestorePointService
{
    private readonly ILoggingService _log;

    public RestorePointService(ILoggingService log) => _log = log;

    public Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                // Allow more than one restore point per day for this session when possible
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
                    key?.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Could not set SystemRestorePointCreationFrequency: {ex.Message}");
                }

                using var sysRestore = new ManagementClass(@"\\.\root\default:SystemRestore");
                var inParams = sysRestore.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = 0; // APPLICATION_INSTALL / MODIFY_SETTINGS style
                inParams["EventType"] = 100; // BEGIN_SYSTEM_CHANGE
                var outParams = sysRestore.InvokeMethod("CreateRestorePoint", inParams, null);
                var status = Convert.ToInt32(outParams?["ReturnValue"] ?? -1);
                if (status == 0)
                {
                    _log.Info($"System restore point created: {description}");
                    return new RestorePointResult { Success = true, Message = "System restore point created." };
                }

                var msg = status switch
                {
                    1058 => "System Protection appears disabled or the Volume Shadow Copy service is unavailable.",
                    _ => $"CreateRestorePoint returned {status}."
                };
                _log.Warn(msg);
                return new RestorePointResult { Success = false, IsWarning = true, Message = msg };
            }
            catch (Exception ex)
            {
                _log.Warn($"Restore point failed: {ex.Message}");
                return new RestorePointResult
                {
                    Success = false,
                    IsWarning = true,
                    Message = $"Could not create restore point: {ex.Message}. JSON backups remain available."
                };
            }
        }, cancellationToken);
    }
}
