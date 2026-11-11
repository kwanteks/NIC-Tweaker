using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

/// <summary>Maps GroupBoxes to tweak Ids. Each Id appears in exactly one group.</summary>
public static class UiGroups
{
    // Per-adapter RSS (Get-NetAdapterRss + a few class-key flags)
    public static IReadOnlyList<TweakDefinition> RssSettings => Ids(
        "rss.status", "rss.profile", "rss.queues", "rss.baseproc", "rss.maxproc", "rss.maxprocs",
        "rss.disableportscaling", "rss.manycore");

    // OS Tcpip/NDIS RssBaseCpu only
    public static IReadOnlyList<TweakDefinition> RssGlobal => Ids("rssg.tcpip", "rssg.ndis");

    // Device interrupt affinity / MSI (Enum\...\Interrupt Management) — not NIC InterruptModeration
    public static IReadOnlyList<TweakDefinition> Interrupt => Ids("int.msi", "int.priority", "int.policy");

    // Adapter power / wake / EEE / PM* offloads / vendor power keywords
    public static IReadOnlyList<TweakDefinition> PowerSaving =>
        TweakCatalog.ForSection(TweakSection.PowerSaving).ToList();

    // OS-wide NetOffloadGlobal + TCP/multimedia knobs (not per-adapter offloads)
    public static IReadOnlyList<TweakDefinition> GlobalSettings => Ids(
        "global.rss", "global.rsc", "global.chimney", "global.taskoffload",
        "global.networkdirect", "global.networkdirect.subnets", "global.pcf",
        "global.congestion", "global.tcpack", "global.loopbackmtu", "global.throttle");

    public static IReadOnlyList<TweakDefinition> InterfaceSettings =>
        TweakCatalog.ForSection(TweakSection.InterfaceSettings).ToList();

    // Link / frame features
    public static IReadOnlyList<TweakDefinition> AdapterFeatures => Ids(
        "adv.flow", "adv.priorityvlan", "adv.jumbo", "adv.packetdirect", "adv.udptx");

    // Buffers, NIC interrupt moderation (not MSI DevicePolicy), coalesce, adapter RSC
    public static IReadOnlyList<TweakDefinition> BuffersCoalesce => Ids(
        "adv.rxbuf", "adv.txbuf", "adv.intmod", "adv.itr", "adv.txintdelay",
        "adv.coalesce", "adv.coalescebuf", "adv.rsciPv4", "adv.rsciPv6", "adv.udprsc", "adv.forcersc");

    // Checksum / LSO / USO / encap / IPsec only (PM* live under PowerSaving)
    public static IReadOnlyList<TweakDefinition> OffloadSettings => Ids(
        "adv.ip4csum", "adv.tcp4csum", "adv.tcp6csum", "adv.udp4csum", "adv.udp6csum",
        "adv.tcpudp4", "adv.tcpudp6",
        "adv.lsov1", "adv.lsov2v4", "adv.lsov2v6",
        "adv.usov4", "adv.usov6",
        "adv.encap", "adv.encap.nvgre", "adv.encap.vxlan",
        "adv.ipsecv1", "adv.ipsecv2v4", "adv.ipsecv2");

    public static IReadOnlyList<TweakDefinition> AfdTweaks =>
        TweakCatalog.ForSection(TweakSection.AfdTweaks).ToList();

    private static IReadOnlyList<TweakDefinition> Ids(params string[] ids) =>
        ids.Select(TweakCatalog.Get).Where(d => d is not null).Cast<TweakDefinition>().ToList();
}
