using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

/// <summary>Maps screenshot GroupBoxes to tweak Ids without splitting the catalog sections.</summary>
public static class UiGroups
{
    public static IReadOnlyList<TweakDefinition> RssSettings => Ids(
        "rss.status", "rss.profile", "rss.queues", "rss.baseproc", "rss.maxproc", "rss.maxprocs",
        "rss.disableportscaling", "rss.manycore");

    public static IReadOnlyList<TweakDefinition> RssGlobal => Ids("rssg.tcpip", "rssg.ndis");

    public static IReadOnlyList<TweakDefinition> Interrupt => Ids("int.msi", "int.priority", "int.policy");

    public static IReadOnlyList<TweakDefinition> PowerSaving =>
        TweakCatalog.ForSection(TweakSection.PowerSaving).ToList();

    public static IReadOnlyList<TweakDefinition> GlobalSettings => Ids(
        "global.rsc", "global.pcf", "global.chimney", "global.taskoffload",
        "global.networkdirect", "global.networkdirect.subnets",
        "global.congestion", "global.tcpack", "global.loopbackmtu", "global.throttle",
        "global.rss");

    public static IReadOnlyList<TweakDefinition> InterfaceSettings =>
        TweakCatalog.ForSection(TweakSection.InterfaceSettings).ToList();

    public static IReadOnlyList<TweakDefinition> AdapterFeatures => Ids(
        "adv.flow", "adv.priorityvlan", "adv.jumbo", "adv.packetdirect", "adv.udptx");

    public static IReadOnlyList<TweakDefinition> BuffersCoalesce => Ids(
        "adv.rxbuf", "adv.txbuf", "adv.intmod", "adv.itr", "adv.txintdelay",
        "adv.coalesce", "adv.rsciPv4", "adv.rsciPv6", "adv.udprsc", "adv.forcersc", "adv.coalescebuf");

    public static IReadOnlyList<TweakDefinition> OffloadSettings => Ids(
        "adv.ip4csum", "adv.tcp4csum", "adv.tcp6csum", "adv.udp4csum", "adv.udp6csum",
        "adv.tcpudp4", "adv.tcpudp6", "adv.lsov1", "adv.lsov2v4", "adv.lsov2v6",
        "adv.usov4", "adv.usov6", "adv.pmarp", "adv.pmns");

    public static IReadOnlyList<TweakDefinition> AfdTweaks =>
        TweakCatalog.ForSection(TweakSection.AfdTweaks).ToList();

    private static IReadOnlyList<TweakDefinition> Ids(params string[] ids) =>
        ids.Select(TweakCatalog.Get).Where(d => d is not null).Cast<TweakDefinition>().ToList();
}
