using Kwanteks.NicTweaker.Models;

namespace Kwanteks.NicTweaker.Services;

public static class TweakCatalog
{
    private static readonly TweakOption[] OffOn =
    [
        new("0 - Disabled", "0"),
        new("1 - Enabled", "1")
    ];

    private static readonly TweakOption[] OffOnTxRx =
    [
        new("0 - Disabled", "0"),
        new("1 - Tx Enabled", "1"),
        new("2 - Rx Enabled", "2"),
        new("3 - Rx and Tx Enabled", "3")
    ];

    private static readonly TweakOption[] DisabledEnabled =
    [
        new("Disabled", "0"),
        new("Enabled", "1")
    ];

    // Match St1icky / Get-NetOffloadGlobalSetting display strings exactly.
    private static readonly TweakOption[] EnabledDisabledOffload =
    [
        new("Disabled", "Disabled"),
        new("Enabled", "Enabled")
    ];

    private static readonly IReadOnlyList<TweakDefinition> AllInternal = Build();

    public static IReadOnlyList<TweakDefinition> All => AllInternal;

    public static IEnumerable<TweakDefinition> ForSection(TweakSection section) =>
        AllInternal.Where(t => t.Section == section);

    public static TweakDefinition? Get(string id) =>
        AllInternal.FirstOrDefault(t => t.Id == id);

    private static IReadOnlyList<TweakDefinition> Build()
    {
        var list = new List<TweakDefinition>();

        // RSS Settings (cmdlet-backed + two registry)
        list.Add(Rss("rss.status", "RSS", "Enabled", "Enable or disable RSS on the selected adapter.", RiskLevel.Medium,
            options: [new("Enable", "true"), new("Disable", "false")]));
        list.Add(Rss("rss.queues", "NumberOfReceiveQueues", "NumberOfReceiveQueues", "Number of RSS receive queues.", RiskLevel.Medium,
            options: Enumerable.Range(1, 16).Select(i => new TweakOption(i.ToString(), i.ToString())).ToArray()));
        list.Add(Rss("rss.profile", "Profile", "Profile", "RSS load-balancing profile.", RiskLevel.Low,
            options:
            [
                new("Closest", "Closest"),
                new("ClosestStatic", "ClosestStatic"),
                new("NUMA", "NUMA"),
                new("NUMAStatic", "NUMAStatic"),
                new("Conservative", "Conservative")
            ]));
        list.Add(Rss("rss.baseproc", "BaseProcessor", "BaseProcessorNumber", "First processor used for RSS.", RiskLevel.Medium,
            options: Enumerable.Range(0, 16).Select(i => new TweakOption(i.ToString(), i.ToString())).ToArray()));
        list.Add(Rss("rss.maxproc", "MaxProcessorNumber", "MaxProcessorNumber", "Highest processor number for RSS (not the same as MaxProcessors).", RiskLevel.Medium,
            options: Enumerable.Range(0, 16).Select(i => new TweakOption(i.ToString(), i.ToString())).ToArray()));
        list.Add(Rss("rss.maxprocs", "MaxProcessors", "MaxProcessors", "Maximum count of processors for RSS.", RiskLevel.Medium,
            options: Enumerable.Range(0, 16).Select(i => new TweakOption(i.ToString(), i.ToString())).ToArray()));

        list.Add(Reg(TweakSection.RssSettings, "rss.disableportscaling", "DisablePortScaling",
            PathTemplates.AdapterClassKey, "DisablePortScaling", TweakValueType.DWord, DisabledEnabled,
            "Disables port scaling on multi-port NICs.", RiskLevel.Medium, RestartRequirement.Adapter, supportsDelete: true, notDriverDeclared: true));
        list.Add(Reg(TweakSection.RssSettings, "rss.manycore", "ManyCoreScaling",
            PathTemplates.AdapterClassKey, "ManyCoreScaling", TweakValueType.DWord, DisabledEnabled,
            "Enables many-core scaling for RSS.", RiskLevel.Medium, RestartRequirement.Adapter, supportsDelete: true, notDriverDeclared: true));

        // Global
        list.Add(Offload("global.rss", "ReceiveSideScaling (OS)", "ReceiveSideScaling", "OS-wide RSS (not the per-adapter RSS box)."));
        list.Add(Offload("global.rsc", "ReceiveSegmentCoalescing (OS)", "ReceiveSegmentCoalescing", "OS-wide RSC (adapter *RscIPv4/v6 live under Buffers)."));
        list.Add(Offload("global.chimney", "Chimney", "Chimney", "TCP Chimney offload (legacy)."));
        list.Add(Offload("global.taskoffload", "TaskOffload", "TaskOffload", "OS-wide task offload."));
        list.Add(new TweakDefinition
        {
            Id = "global.networkdirect",
            Section = TweakSection.GlobalSettings,
            Label = "NetworkDirect",
            PathTemplate = PathTemplates.NdisParameters,
            ValueName = "NetworkDirectDisable",
            ValueType = TweakValueType.DWord,
            Backend = TweakBackend.Registry,
            Options = [new("Enabled", "delete"), new("Disabled", "1")],
            SupportsDelete = true,
            Description = "Controls NetworkDirect via NDIS NetworkDirectDisable (1=disabled).",
            RiskLevel = RiskLevel.Medium,
            RequiresRestart = RestartRequirement.Reboot
        });
        list.Add(new TweakDefinition
        {
            Id = "global.networkdirect.subnets",
            Section = TweakSection.GlobalSettings,
            Label = "NetworkDirectAcrossIPSubnets",
            PathTemplate = PathTemplates.NdisParameters,
            ValueName = "NetworkDirectGlobalFlags",
            ValueType = TweakValueType.DWord,
            Backend = TweakBackend.Registry,
            Options = [new("Blocked", "delete"), new("Allowed", "1")],
            SupportsDelete = true,
            Description = "NetworkDirect across IP subnets via NetworkDirectGlobalFlags.",
            RiskLevel = RiskLevel.Medium,
            RequiresRestart = RestartRequirement.Reboot
        });
        list.Add(Offload("global.pcf", "PacketCoalescingFilter", "PacketCoalescingFilter", "Packet coalescing filter."));

        // Adv Adapter
        void Adv(string id, string label, string valueName, TweakValueType type, TweakOption[] options, string desc, bool del = false, bool free = false) =>
            list.Add(Reg(TweakSection.AdvAdapter, id, label, PathTemplates.AdapterClassKey, valueName, type, options, desc, RiskLevel.Medium, RestartRequirement.Adapter, del, free));

        Adv("adv.flow", "FlowControl", "*FlowControl", TweakValueType.String, OffOnTxRx, "Ethernet flow control.");
        Adv("adv.priorityvlan", "PriorityVLANTag", "*PriorityVLANTag", TweakValueType.String,
        [
            new("0 - Packet Priority and VLAN disabled", "0"),
            new("1 - Packet Priority enabled", "1"),
            new("2 - VLAN enabled", "2"),
            new("3 - Packet Priority and VLAN enabled", "3")
        ], "802.1p/Q priority and VLAN tagging.");
        Adv("adv.jumbo", "JumboPacket", "*JumboPacket", TweakValueType.String, [], "Jumbo frame size.", del: true, free: true);
        Adv("adv.packetdirect", "PacketDirect", "*PacketDirect", TweakValueType.String,
            [new("0 - Disabled", "0"), new("1 - Enabled", "1")], "Packet Direct.", del: true);
        Adv("adv.udptx", "UdpTxScaling", "EnableUdpTxScaling", TweakValueType.DWord, DisabledEnabled, "UDP TX scaling.", del: true);

        Adv("adv.rxbuf", "ReceiveBuffers", "*ReceiveBuffers", TweakValueType.String, [], "Receive buffer count.", free: true);
        Adv("adv.txbuf", "TransmitBuffers", "*TransmitBuffers", TweakValueType.String, [], "Transmit buffer count.", free: true);
        Adv("adv.intmod", "InterruptModeration", "*InterruptModeration", TweakValueType.String, OffOn, "Interrupt moderation.");
        Adv("adv.itr", "InterruptModerationRate", "ITR", TweakValueType.DWord,
        [
            new("0 - Disabled", "0"),
            new("200 - Minimal", "200"),
            new("400 - Low", "400"),
            new("950 - Medium", "950"),
            new("2000 - High", "2000"),
            new("3600 - Extreme", "3600"),
            new("65535 - Adaptive", "65535")
        ], "Interrupt moderation rate (ITR).");
        Adv("adv.txintdelay", "TxIntDelay", "TxIntDelay", TweakValueType.String, [], "Transmit interrupt delay.", free: true);
        Adv("adv.coalesce", "Coalesce", "EnableCoalesce", TweakValueType.DWord, DisabledEnabled, "Enable coalesce.", del: true);
        Adv("adv.coalescebuf", "CoalesceBufferSize", "CoalesceBufferSize", TweakValueType.DWord,
            [new("2048", "2048")], "Coalesce buffer size.", del: true, free: true);
        Adv("adv.rsciPv4", "RscIPv4", "*RscIPv4", TweakValueType.String, OffOn, "Receive Segment Coalescing IPv4.", del: true);
        Adv("adv.rsciPv6", "RscIPv6", "*RscIPv6", TweakValueType.String, OffOn, "Receive Segment Coalescing IPv6.", del: true);
        Adv("adv.udprsc", "UdpRsc", "*UdpRsc", TweakValueType.String, OffOn, "UDP RSC.", del: true);
        Adv("adv.forcersc", "ForceRscEnabled", "ForceRscEnabled", TweakValueType.DWord, DisabledEnabled, "Force RSC enabled.", del: true);

        Adv("adv.ip4csum", "IPChecksumOffloadIPv4", "*IPChecksumOffloadIPv4", TweakValueType.String, OffOnTxRx, "IPv4 IP checksum offload.");
        Adv("adv.tcp4csum", "TCPChecksumOffloadIPv4", "*TCPChecksumOffloadIPv4", TweakValueType.String, OffOnTxRx, "IPv4 TCP checksum offload.");
        Adv("adv.tcp6csum", "TCPChecksumOffloadIPv6", "*TCPChecksumOffloadIPv6", TweakValueType.String, OffOnTxRx, "IPv6 TCP checksum offload.");
        Adv("adv.udp4csum", "UDPChecksumOffloadIPv4", "*UDPChecksumOffloadIPv4", TweakValueType.String, OffOnTxRx, "IPv4 UDP checksum offload.");
        Adv("adv.udp6csum", "UDPChecksumOffloadIPv6", "*UDPChecksumOffloadIPv6", TweakValueType.String, OffOnTxRx, "IPv6 UDP checksum offload.");
        Adv("adv.tcpudp4", "TCPUDPChecksumOffloadIPv4", "*TCPUDPChecksumOffloadIPv4", TweakValueType.String, OffOnTxRx, "Combined TCP/UDP checksum offload IPv4.", del: true);
        Adv("adv.tcpudp6", "TCPUDPChecksumOffloadIPv6", "*TCPUDPChecksumOffloadIPv6", TweakValueType.String, OffOnTxRx, "Combined TCP/UDP checksum offload IPv6.", del: true);
        Adv("adv.lsov1", "LsoV1IPv4", "*LsoV1IPv4", TweakValueType.String, OffOn, "Large Send Offload v1 IPv4.");
        Adv("adv.lsov2v4", "LsoV2IPv4", "*LsoV2IPv4", TweakValueType.String, OffOn, "Large Send Offload v2 IPv4.");
        Adv("adv.lsov2v6", "LsoV2IPv6", "*LsoV2IPv6", TweakValueType.String, OffOn, "Large Send Offload v2 IPv6.");
        Adv("adv.usov4", "UsoIPv4", "*UsoIPv4", TweakValueType.String, OffOn, "UDP Segmentation Offload IPv4.", del: true);
        Adv("adv.usov6", "UsoIPv6", "*UsoIPv6", TweakValueType.String, OffOn, "UDP Segmentation Offload IPv6.", del: true);
        Adv("adv.encap", "EncapsulatedPacketTaskOffload", "*EncapsulatedPacketTaskOffload", TweakValueType.String, OffOn, "Encapsulated packet task offload.", del: true);
        Adv("adv.encap.nvgre", "EncapsulatedPacketTaskOffloadNvgre", "*EncapsulatedPacketTaskOffloadNvgre", TweakValueType.String, OffOn, "NVGRE encapsulated offload.", del: true);
        Adv("adv.encap.vxlan", "EncapsulatedPacketTaskOffloadVxlan", "*EncapsulatedPacketTaskOffloadVxlan", TweakValueType.String, OffOn, "VXLAN encapsulated offload.", del: true);
        Adv("adv.ipsecv1", "IPsecOffloadV1IPv4", "*IPsecOffloadV1IPv4", TweakValueType.String, OffOnTxRx, "IPsec offload v1 IPv4.", del: true);
        Adv("adv.ipsecv2v4", "IPsecOffloadV2IPv4", "*IPsecOffloadV2IPv4", TweakValueType.String, OffOnTxRx, "IPsec offload v2 IPv4.", del: true);
        Adv("adv.ipsecv2", "IPsecOffloadV2", "*IPsecOffloadV2", TweakValueType.String, OffOnTxRx, "IPsec offload v2.", del: true);

        // RSS Global
        list.Add(Reg(TweakSection.RssGlobal, "rssg.tcpip", "TCP/IP_RssBaseCpu",
            PathTemplates.TcpipParameters, "RssBaseCpu", TweakValueType.DWord, [],
            "TCP/IP RssBaseCpu.", RiskLevel.Medium, RestartRequirement.Reboot, supportsDelete: true, allowFreeText: true, min: 0, max: 256));
        list.Add(Reg(TweakSection.RssGlobal, "rssg.ndis", "NDIS_RssBaseCpu",
            PathTemplates.NdisParameters, "RssBaseCpu", TweakValueType.DWord, [],
            "NDIS RssBaseCpu.", RiskLevel.Medium, RestartRequirement.Reboot, supportsDelete: true, allowFreeText: true, min: 0, max: 256));

        // Power saving
        void Pow(string id, string label, string name, TweakValueType type, TweakOption[] opts, string desc, bool free = false, int? min = null, int? max = null) =>
            list.Add(Reg(TweakSection.PowerSaving, id, label, PathTemplates.AdapterClassKey, name, type, opts, desc, RiskLevel.Low, RestartRequirement.Adapter, supportsDelete: true, allowFreeText: free, min: min, max: max));

        Pow("pow.pme", "(APM) sleep states", "EnablePME", TweakValueType.DWord, DisabledEnabled, "Enable PME / APM sleep states.");
        Pow("pow.selsuspend", "SelectiveSuspend", "*SelectiveSuspend", TweakValueType.String, DisabledEnabled, "NIC selective suspend.");
        Pow("pow.ssidle", "SSIdleTimeout", "SSIdleTimeout", TweakValueType.DWord, [], "Selective suspend idle timeout (seconds).", free: true, min: 0, max: 100000);
        Pow("pow.dyn", "DynamicPowerGating", "*EnableDynamicPowerGating", TweakValueType.String, DisabledEnabled, "Dynamic power gating.");
        Pow("pow.conn", "ConnectedPowerGating", "EnableConnectedPowerGating", TweakValueType.DWord, DisabledEnabled, "Connected standby power gating.");
        Pow("pow.auto", "AutoPowerSaveMode", "AutoPowerSaveModeEnabled", TweakValueType.DWord, DisabledEnabled, "Auto power save mode.");
        Pow("pow.nic", "NicAutoPowerSaver", "*NicAutoPowerSaver", TweakValueType.String, DisabledEnabled, "NIC auto power saver.");
        Pow("pow.delayed", "DelayedPowerUp", "DisableDelayedPowerUp", TweakValueType.DWord,
            [new("Enabled", "0"), new("Disabled", "1")], "Delayed power-up (registry DisableDelayedPowerUp is inverted).");
        Pow("pow.reduce", "ReduceSpeedOnPowerDown", "ReduceSpeedOnPowerDown", TweakValueType.DWord, DisabledEnabled, "Reduce link speed on power down.");
        Pow("pow.wake.magic", "WakeOnMagicPacket", "*WakeOnMagicPacket", TweakValueType.String, DisabledEnabled, "Wake on magic packet.");
        Pow("pow.wake.pattern", "WakeOnPattern", "*WakeOnPattern", TweakValueType.String, DisabledEnabled, "Wake on pattern.");
        // PM* offloads are wake/power-path features (were wrongly listed under Offload Settings).
        Pow("pow.pmarp", "PMARPOffload", "*PMARPOffload", TweakValueType.String, OffOn, "ARP offload for power management.");
        Pow("pow.pmns", "PMNSOffload", "*PMNSOffload", TweakValueType.String, OffOn, "NS offload for power management.");
        Pow("pow.pmwifi", "PMWiFiRekeyOffload", "*PMWiFiRekeyOffload", TweakValueType.String, OffOn, "Wi-Fi rekey offload.");
        Pow("pow.eee", "EEE", "*EEE", TweakValueType.String, DisabledEnabled, "Energy Efficient Ethernet.");
        Pow("pow.eee.link", "EEELinkAdvertisement", "*EEELinkAdvertisement", TweakValueType.String, DisabledEnabled, "EEE link advertisement.");
        Pow("pow.sleep.disconnect", "DeviceSleepOnDisconnect", "*DeviceSleepOnDisconnect", TweakValueType.String, DisabledEnabled, "Sleep on media disconnect.");
        Pow("pow.green", "EnableGreenEthernet", "EnableGreenEthernet", TweakValueType.DWord, DisabledEnabled, "Green Ethernet.");
        // Vendor power-saving keywords (exact ValueName; unsupported drivers show N/A)
        Pow("pow.advancedeee", "AdvancedEEE", "AdvancedEEE", TweakValueType.String, DisabledEnabled, "Realtek Advanced EEE.");
        Pow("pow.powersaving", "PowerSavingMode", "PowerSavingMode", TweakValueType.String, DisabledEnabled, "Realtek power saving mode.");
        Pow("pow.gigalite", "GigaLite", "GigaLite", TweakValueType.String, DisabledEnabled, "Realtek GigaLite.");
        Pow("pow.autodisablegigabit", "AutoDisableGigabit", "AutoDisableGigabit", TweakValueType.String, DisabledEnabled, "Realtek auto-disable Gigabit.");
        Pow("pow.s5wol", "S5WakeOnLan", "S5WakeOnLan", TweakValueType.String, DisabledEnabled, "Wake on LAN from S5 / shutdown.");
        Pow("pow.wolshutdownspeed", "WolShutdownLinkSpeed", "WolShutdownLinkSpeed", TweakValueType.String,
            [new("0", "0"), new("1", "1"), new("2", "2")], "Link speed used for WOL after shutdown.");
        Pow("pow.ulpmode", "ULPMode", "ULPMode", TweakValueType.String, DisabledEnabled, "Intel Ultra Low Power mode.");
        Pow("pow.sips", "SipsEnabled", "SipsEnabled", TweakValueType.String, DisabledEnabled, "Intel System Idle Power Saver.");

        // Extra global TCP/NetBT registry knobs (Apply NetBT / TCP button)
        list.Add(Reg(TweakSection.GlobalSettings, "global.congestion", "CongestionProvider",
            PathTemplates.TcpipParameters, "CongestionProvider", TweakValueType.String,
            [new("Default", "Default"), new("CTCP", "CTCP"), new("CUBIC", "CUBIC"), new("NewReno", "NewReno")],
            "TCP congestion provider.", RiskLevel.Medium, RestartRequirement.Reboot, supportsDelete: true));
        list.Add(Reg(TweakSection.GlobalSettings, "global.tcpack", "TcpAckFrequency",
            PathTemplates.TcpipParameters, "TcpAckFrequency", TweakValueType.DWord, [],
            "TCP ACK frequency.", RiskLevel.Medium, RestartRequirement.Reboot, supportsDelete: true, allowFreeText: true, min: 1, max: 255));
        list.Add(Reg(TweakSection.GlobalSettings, "global.loopbackmtu", "LoopbackLargeMtu",
            PathTemplates.TcpipParameters, "EnableLoopbackLargeMtu", TweakValueType.DWord, DisabledEnabled,
            "Loopback large MTU.", RiskLevel.Low, RestartRequirement.Reboot, supportsDelete: true));
        list.Add(Reg(TweakSection.GlobalSettings, "global.throttle", "NetworkThrottlingIndex",
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", TweakValueType.DWord, [],
            "Network throttling index (0xFFFFFFFF = disabled).", RiskLevel.Medium, RestartRequirement.Reboot, supportsDelete: true, allowFreeText: true));

        // Interface settings
        var enabledDisabled = new TweakOption[] { new("Enabled", "Enabled"), new("Disabled", "Disabled") };
        string[] writableEnums =
        [
            "AdvertiseDefaultRoute", "Advertising", "AutomaticMetric", "ClampMss", "DirectedMacWolPattern",
            "EcnMarking", "ForceArpNdWolPattern", "Forwarding", "IgnoreDefaultRoutes", "ManagedAddressConfiguration",
            "NeighborUnreachabilityDetection", "OtherStatefulConfiguration", "RouterDiscovery", "Store",
            "WeakHostReceive", "WeakHostSend"
        ];
        foreach (var p in writableEnums)
        {
            TweakOption[] opts = p switch
            {
                "EcnMarking" => [new("AppDecide", "AppDecide"), new("Disabled", "Disabled"), new("UseEct1", "UseEct1"), new("UseEct0", "UseEct0")],
                "RouterDiscovery" => [new("Disabled", "Disabled"), new("Enabled", "Enabled"), new("ControlledByDHCP", "ControlledByDHCP")],
                "Store" => [new("ActiveStore", "Active"), new("PersistentStore", "Persistent")],
                _ => enabledDisabled
            };

            var label = p switch
            {
                "NeighborUnreachabilityDetection" => "NeighborUnreachDetection",
                _ => p
            };

            list.Add(new TweakDefinition
            {
                Id = $"iface.{p.ToLowerInvariant()}",
                Section = TweakSection.InterfaceSettings,
                Label = label,
                Backend = TweakBackend.NetIpInterface,
                NetIpProperty = p,
                ValueType = TweakValueType.Enum,
                Options = opts,
                Description = $"NetIPInterface.{p}",
                RiskLevel = RiskLevel.Medium,
                RequiresRestart = RestartRequirement.None
            });
        }

        list.Add(new TweakDefinition
        {
            Id = "iface.neighbordiscoverysupported",
            Section = TweakSection.InterfaceSettings,
            Label = "NeighborDiscoverySupported",
            Backend = TweakBackend.NetIpInterface,
            NetIpProperty = "NeighborDiscoverySupported",
            ValueType = TweakValueType.Enum,
            Options = [new("Yes", "True"), new("No", "False"), new("Yes", "yes"), new("No", "no")],
            IsReadOnly = true,
            Description = "Read-only on most systems.",
            RiskLevel = RiskLevel.Low
        });
        list.Add(new TweakDefinition
        {
            Id = "iface.reachabletime",
            Section = TweakSection.InterfaceSettings,
            Label = "ReachableTime",
            Backend = TweakBackend.NetIpInterface,
            NetIpProperty = "ReachableTime",
            ValueType = TweakValueType.Enum,
            IsReadOnly = true,
            AllowFreeText = true,
            Description = "Read-only reachable time (ms).",
            RiskLevel = RiskLevel.Low
        });

        void IfaceNum(string prop, string label, int? min, int? max, string unit) =>
            list.Add(new TweakDefinition
            {
                Id = $"iface.{prop.ToLowerInvariant()}",
                Section = TweakSection.InterfaceSettings,
                Label = label,
                Backend = TweakBackend.NetIpInterface,
                NetIpProperty = prop,
                ValueType = TweakValueType.DWord,
                AllowFreeText = true,
                MinValue = min,
                MaxValue = max,
                Description = $"{prop} ({unit}).",
                RiskLevel = RiskLevel.Medium
            });

        IfaceNum("CurrentHopLimit", "CurrentHopLimit", 0, 255, "hops; 0 = OS default");
        IfaceNum("BaseReachableTime", "BaseReachableTime", 0, int.MaxValue, "ms");
        IfaceNum("RetransmitTime", "RetransmitTime", 0, int.MaxValue, "ms");
        IfaceNum("DadRetransmitTime", "DadRetransmitTime", 0, int.MaxValue, "ms");
        IfaceNum("DadTransmits", "DadTransmits", 0, 100, "count");
        IfaceNum("NlMtu", "MTU", 0, int.MaxValue, "bytes");

        // Interrupt
        list.Add(Reg(TweakSection.InterruptSettings, "int.msi", "MSI Mode",
            PathTemplates.InterruptBase + @"\MessageSignaledInterruptProperties", "MSISupported",
            TweakValueType.DWord, DisabledEnabled, "Message-signaled interrupts.", RiskLevel.High, RestartRequirement.Reboot));
        list.Add(Reg(TweakSection.InterruptSettings, "int.priority", "Interrupt Priority",
            PathTemplates.InterruptBase + @"\Affinity Policy", "DevicePriority",
            TweakValueType.DWord,
            [new("Undefined", "0"), new("Low", "1"), new("Normal", "2"), new("High", "3")],
            "Device interrupt priority.", RiskLevel.High, RestartRequirement.Reboot));
        list.Add(Reg(TweakSection.InterruptSettings, "int.policy", "DevicePolicy",
            PathTemplates.InterruptBase + @"\Affinity Policy", "DevicePolicy",
            TweakValueType.DWord,
            [
                new("MachineDefault", "0"),
                new("AllCloseProcessors", "1"),
                new("OneCloseProcessor", "2"),
                new("AllProcessorsInMachine", "3"),
                new("SpecifiedProcessors", "4"),
                new("SpreadMessagesAcrossAllProcessors", "5")
            ],
            "Interrupt affinity device policy.", RiskLevel.High, RestartRequirement.Reboot));

        // AFD
        void Afd(string id, string label, string name, TweakOption[] opts, string desc, bool free = false, int? min = null, int? max = null) =>
            list.Add(Reg(TweakSection.AfdTweaks, id, label, PathTemplates.AfdParameters, name, TweakValueType.DWord, opts, desc,
                RiskLevel.High, RestartRequirement.Reboot, supportsDelete: true, allowFreeText: free || opts.Length == 0, min: min, max: max));

        Afd("afd.recv", "DefaultReceiveWindow", "DefaultReceiveWindow", [new("8192", "8192"), new("32767", "32767")], "AFD default receive window.", free: true, min: 0, max: int.MaxValue);
        Afd("afd.send", "DefaultSendWindow", "DefaultSendWindow", [new("8192", "8192"), new("32767", "32767")], "AFD default send window.", free: true, min: 0, max: int.MaxValue);
        Afd("afd.bufmul", "(Rec/Send) BufferMultiplier", "BufferMultiplier", [new("512", "512")], "Buffer multiplier.", free: true);
        Afd("afd.bufalign", "BufferAlignment", "BufferAlignment", [new("0", "0"), new("1", "1")], "Buffer alignment.");
        Afd("afd.hold", "DoNotHoldNICBuffers", "DoNotHoldNICBuffers", [new("0", "0"), new("1", "1")], "Do not hold NIC buffers.");
        Afd("afd.small", "SmallBufferSize", "SmallBufferSize", [new("128", "128")], "Small buffer size.", free: true);
        Afd("afd.med", "MediumBufferSize", "MediumBufferSize", [new("1504", "1504")], "Medium buffer size.", free: true);
        Afd("afd.large", "LargeBufferSize", "LargeBufferSize", [new("3876", "3876")], "Large buffer size.", free: true);
        Afd("afd.huge", "HugeBufferSize", "HugeBufferSize", [], "Huge buffer size.", free: true);
        Afd("afd.smalldepth", "SmallBufferListDepth", "SmallBufferListDepth", [new("8", "8"), new("16", "16")], "Small buffer list depth.");
        Afd("afd.meddepth", "MediumBufferListDepth", "MediumBufferListDepth", [new("4", "4"), new("8", "8"), new("16", "16")], "Medium buffer list depth.");
        Afd("afd.largdepth", "LargeBufferListDepth", "LargBufferListDepth", [new("0", "0"), new("2", "2"), new("10", "10")], "Large buffer list depth (Windows key spelling: LargBufferListDepth).");
        Afd("afd.noshare", "DisableAddressSharing", "DisableAddressSharing", [new("0", "0"), new("1", "1")], "Disable address sharing.");
        Afd("afd.nochained", "DisableChainedReceive", "DisableChainedReceive", [new("0", "0"), new("1", "1")], "Disable chained receive.");
        Afd("afd.nodirect", "DisableDirectAcceptEx", "DisableDirectAcceptEx", [new("0", "0"), new("1", "1")], "Disable Direct AcceptEx.");
        Afd("afd.noraw", "DisableRawSecurity", "DisableRawSecurity", [new("0", "0"), new("1", "1")], "Disable raw security.");
        Afd("afd.dynsend", "DynamicSendBufferDisable", "DynamicSendBufferDisable", [new("0", "0"), new("1", "1")], "Disable dynamic send buffer.");
        Afd("afd.fastsend", "FastSendDatagramThreshold", "FastSendDatagramThreshold", [new("1024", "1024")], "Fast send datagram threshold.", free: true);
        Afd("afd.fastcopy", "FastCopyReceiveThreshold", "FastCopyReceiveThreshold", [new("1024", "1024")], "Fast copy receive threshold.", free: true);
        Afd("afd.ignorepush", "IgnorePushBitOnReceives", "IgnorePushBitOnReceives", [new("0", "0"), new("1", "1")], "Ignore PUSH bit on receives.");
        Afd("afd.ignoreorder", "IgnoreOrderlyRelease", "IgnoreOrderlyRelease", [new("0", "0"), new("1", "1")], "Ignore orderly release.");
        Afd("afd.txworker", "TransmitWorker", "TransmitWorker", [new("16", "16"), new("32", "32")], "Transmit worker count.");
        Afd("afd.prioboost", "PriorityBoost", "PriorityBoost", [new("0", "0"), new("1", "1"), new("2", "2")], "Priority boost.");

        return list;
    }

    private static TweakDefinition Rss(string id, string label, string prop, string desc, RiskLevel risk, TweakOption[]? options = null) =>
        new()
        {
            Id = id,
            Section = TweakSection.RssSettings,
            Label = label,
            Backend = TweakBackend.NetAdapterRss,
            RssProperty = prop,
            ValueType = TweakValueType.Enum,
            Options = options ?? Array.Empty<TweakOption>(),
            Description = desc,
            RiskLevel = risk,
            RequiresRestart = RestartRequirement.Adapter,
            AllowFreeText = options is null || options.Length == 0
        };

    private static TweakDefinition Offload(string id, string label, string prop, string desc) =>
        new()
        {
            Id = id,
            Section = TweakSection.GlobalSettings,
            Label = label,
            Backend = TweakBackend.NetOffloadGlobal,
            OffloadProperty = prop,
            ValueType = TweakValueType.Enum,
            Options = EnabledDisabledOffload,
            Description = desc,
            RiskLevel = RiskLevel.Medium,
            RequiresRestart = RestartRequirement.Reboot
        };

    private static TweakDefinition Reg(
        TweakSection section,
        string id,
        string label,
        string path,
        string valueName,
        TweakValueType type,
        TweakOption[] options,
        string desc,
        RiskLevel risk,
        RestartRequirement restart,
        bool supportsDelete = false,
        bool allowFreeText = false,
        int? min = null,
        int? max = null,
        bool notDriverDeclared = false) =>
        new()
        {
            Id = id,
            Section = section,
            Label = label,
            PathTemplate = path,
            ValueName = valueName,
            ValueType = type,
            Backend = TweakBackend.Registry,
            Options = options,
            SupportsDelete = supportsDelete,
            Description = desc,
            RiskLevel = risk,
            RequiresRestart = restart,
            AllowFreeText = allowFreeText,
            MinValue = min,
            MaxValue = max,
            NotDriverDeclared = notDriverDeclared
        };
}
