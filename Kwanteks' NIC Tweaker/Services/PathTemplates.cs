using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public static class PathTemplates
{
    public const string AdapterClassKey = "{AdapterClassKey}";
    public const string InterruptBase = "{InterruptBase}";
    public const string AfdParameters = @"HKLM\SYSTEM\CurrentControlSet\Services\AFD\Parameters";
    public const string TcpipParameters = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
    public const string NdisParameters = @"HKLM\SYSTEM\CurrentControlSet\Services\NDIS\Parameters";
    public const string Tcpip6Parameters = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";

    public static string Resolve(string? template, AdapterInfo? adapter)
    {
        if (string.IsNullOrWhiteSpace(template))
            return "";

        // An unresolved placeholder would otherwise collapse to "\..." and resolve to a key at the hive root.
        var needsAdapterPath = template.Contains(AdapterClassKey, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(adapter?.ClassKeyPath);
        var needsInterruptPath = template.Contains(InterruptBase, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(adapter?.InterruptBasePath);
        if (needsAdapterPath || needsInterruptPath)
            throw new InvalidOperationException("Adapter path not resolved. Select an adapter first.");

        var path = template
            .Replace(AdapterClassKey, adapter?.ClassKeyPath ?? "", StringComparison.Ordinal)
            .Replace(InterruptBase, adapter?.InterruptBasePath ?? "", StringComparison.Ordinal);

        if (path.Contains(AdapterClassKey, StringComparison.Ordinal) ||
            path.Contains(InterruptBase, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(path.Replace("HKLM\\", "", StringComparison.OrdinalIgnoreCase).Trim('\\')))
            throw new InvalidOperationException("Adapter path not resolved. Select an adapter first.");

        return path;
    }
}
