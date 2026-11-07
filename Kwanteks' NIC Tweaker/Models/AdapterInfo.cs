namespace Kwanteks.NicTweaker.Models;

public sealed class AdapterInfo
{
    public required string InterfaceDescription { get; init; }
    public required string InterfaceAlias { get; init; }
    public required Guid InterfaceGuid { get; init; }
    public required string NetCfgInstanceId { get; init; }
    public string? PnpDeviceId { get; init; }
    public string? NdisVersion { get; set; }
    public string? Status { get; set; }
    public string? ClassKeyPath { get; set; }
    public string? InterruptBasePath { get; set; }

    public override string ToString() => InterfaceDescription;
}
