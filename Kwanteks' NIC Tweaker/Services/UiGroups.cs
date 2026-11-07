using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

/// <summary>Maps GroupBoxes to tweak Ids (script + remake layout).</summary>
public static class UiGroups
{
    // Original RSS Settings (+ DisablePortScaling/ManyCoreScaling live here in the script)
    public static IReadOnlyList<TweakDefinition> RssSettings => Ids(
        "rss.status", "rss.profile", "rss.queues", "rss.baseproc", "rss.maxproc", "rss.maxprocs",
        "rss.disableportscaling", "rss.manycore");

    public static IReadOnlyList<TweakDefinition> RssGlobal => Ids("rssg.tcpip", "rssg.ndis");

    public static IReadOnlyList<TweakDefinition> Interrupt => Ids("int.msi", "int.priority", "int.policy");

    public static IReadOnlyList<TweakDefinition> PowerSaving =>
        TweakCatalog.ForSection(TweakSection.PowerSaving).ToList();

    // Script Global Settings first, then remake TCP/NetBT knobs
    public static IReadOnlyList<TweakDefinition> GlobalSettings => Ids(
        "global.rss", "global.rsc", "global.chimney", "global.taskoffload",
        "global.networkdirect", "global.networkdirect.subnets", "global.pcf",
        "global.congestion", "global.tcpack", "global.loopbackmtu", "global.throttle");

    public static IReadOnlyList<TweakDefinition> InterfaceSettings =>
        TweakCatalog.ForSection(TweakSection.InterfaceSettings).ToList();

    public static IReadOnlyList<TweakDefinition> AdapterFeatures => Ids(
        "adv.flow", "adv.priorityvlan", "adv.jumbo", "adv.packetdirect", "adv.udptx");

    public static IReadOnlyList<TweakDefinition> BuffersCoalesce => Ids(
        "adv.rxbuf", "adv.txbuf", "adv.intmod", "adv.itr", "adv.txintdelay",
        "adv.coalesce", "adv.coalescebuf", "adv.rsciPv4", "adv.rsciPv6", "adv.udprsc", "adv.forcersc");

    // Script Adv offloads + remake USC/IPsec/encap/PM Wi‑Fi
    public static IReadOnlyList<TweakDefinition> OffloadSettings => Ids(
        "adv.ip4csum", "adv.tcp4csum", "adv.tcp6csum", "adv.udp4csum", "adv.udp6csum",
        "adv.tcpudp4", "adv.tcpudp6",
        "adv.lsov1", "adv.lsov2v4", "adv.lsov2v6",
        "adv.usov4", "adv.usov6",
        "adv.encap", "adv.encap.nvgre", "adv.encap.vxlan",
        "adv.ipsecv1", "adv.ipsecv2v4", "adv.ipsecv2",
        "adv.pmarp", "adv.pmns", "adv.pmwifi");

    public static IReadOnlyList<TweakDefinition> AfdTweaks =>
        TweakCatalog.ForSection(TweakSection.AfdTweaks).ToList();

    private static IReadOnlyList<TweakDefinition> Ids(params string[] ids) =>
        ids.Select(TweakCatalog.Get).Where(d => d is not null).Cast<TweakDefinition>().ToList();
}
