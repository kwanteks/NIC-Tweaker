using System.Diagnostics;
using System.Management;
using Kwanteks.NicTweaker.Models;
using Microsoft.Win32;

namespace Kwanteks.NicTweaker.Services;

public sealed class AdapterService : IAdapterService
{
    private readonly IRegistryService _registry;
    private readonly IPowerShellBridge _ps;
    private readonly ILoggingService _log;

    public AdapterService(IRegistryService registry, IPowerShellBridge ps, ILoggingService log)
    {
        _registry = registry;
        _ps = ps;
        _log = log;
    }

    public Task<IReadOnlyList<AdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = new List<AdapterInfo>();

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NetConnectionID, GUID, PNPDeviceID, NetEnabled, AdapterTypeID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True AND NetConnectionStatus IS NOT NULL");
            foreach (ManagementObject mo in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var guidText = mo["GUID"]?.ToString();
                if (string.IsNullOrWhiteSpace(guidText) || !Guid.TryParse(guidText, out var guid))
                    continue;

                var name = mo["Name"]?.ToString() ?? guidText;
                var alias = mo["NetConnectionID"]?.ToString() ?? name;
                var pnp = mo["PNPDeviceID"]?.ToString();

                list.Add(new AdapterInfo
                {
                    InterfaceDescription = name,
                    InterfaceAlias = alias,
                    InterfaceGuid = guid,
                    NetCfgInstanceId = guidText,
                    PnpDeviceId = pnp,
                    Status = mo["NetEnabled"] is bool b ? (b ? "Up" : "Down") : null
                });
            }

            // Prefer Get-NetAdapter for NDIS version when available
            try
            {
                EnrichNdisVersions(list, cancellationToken);
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not read NDIS versions: {ex.Message}");
            }

            return (IReadOnlyList<AdapterInfo>)list
                .OrderBy(a => a.InterfaceDescription, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    private void EnrichNdisVersions(List<AdapterInfo> list, CancellationToken cancellationToken)
    {
        // Lightweight: resolve from registry DriverDesc / Ndi later if needed.
        // NDIS version comes from Get-NetAdapter when PS is available.
        _ = cancellationToken;
        _ = list;
    }

    public Task EnrichPathsAsync(AdapterInfo adapter, CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            adapter.ClassKeyPath = _registry.FindAdapterClassKeyByNetCfgInstanceId(adapter.NetCfgInstanceId);
            if (!string.IsNullOrWhiteSpace(adapter.PnpDeviceId))
            {
                adapter.InterruptBasePath =
                    $@"HKLM\SYSTEM\CurrentControlSet\Enum\{adapter.PnpDeviceId}\Device Parameters\Interrupt Management";
            }

            try
            {
                // Alias is unique per interface; two identical NICs share a description.
                var escaped = Escape(adapter.InterfaceAlias);
                var ndis = await _ps.RunAsync(
                    $"$ErrorActionPreference='Stop'; (Get-NetAdapter -Name '{escaped}' | Select-Object -ExpandProperty NdisVersion)",
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(ndis))
                    adapter.NdisVersion = ndis;

                var status = await _ps.RunAsync(
                    $"$ErrorActionPreference='Stop'; (Get-NetAdapter -Name '{escaped}' | Select-Object -ExpandProperty Status)",
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(status))
                    adapter.Status = status;
            }
            catch (Exception ex)
            {
                _log.Warn($"Enrich adapter failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    public Task OpenRegeditAtAdapterAsync(AdapterInfo adapter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(adapter.ClassKeyPath))
                throw new InvalidOperationException("No adapter registry path resolved.");

            var lastKey = "Computer\\" + adapter.ClassKeyPath.Replace("HKLM\\", "HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase);
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
            key?.SetValue("LastKey", lastKey, RegistryValueKind.String);
            Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }, cancellationToken);
    }

    public async Task RestartAdapterAsync(
        AdapterInfo adapter,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var escaped = Escape(adapter.InterfaceAlias);
        progress?.Report("Restarting adapter…");
        await _ps.RunAsync(
            $"$ErrorActionPreference='Stop'; Restart-NetAdapter -Name '{escaped}' -Confirm:$false",
            cancellationToken);

        const int maxAttempts = 30;
        for (var i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Waiting for adapter… ({i + 1}/{maxAttempts})");
            try
            {
                var status = await _ps.RunAsync(
                    $"$ErrorActionPreference='Stop'; (Get-NetAdapter -Name '{escaped}' | Select-Object -ExpandProperty Status)",
                    cancellationToken);
                if (string.Equals(status, "Up", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report("Adapter is Up.");
                    return;
                }
            }
            catch
            {
                // still coming back
            }

            await Task.Delay(1000, cancellationToken);
        }

        throw new TimeoutException("Adapter did not return to Up status within 30 seconds.");
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
