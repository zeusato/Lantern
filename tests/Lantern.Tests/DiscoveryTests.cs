using System.Net;
using System.Net.Sockets;
using System.Text;
using Lantern.Core;

internal static class DiscoveryTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var network = new LanInterface(IPAddress.Parse("10.10.14.108"), 24);
        var targets = network.ProbeAddresses().ToArray();
        check(network.Broadcast!.ToString() == "10.10.14.255" && targets.Length == 253 && targets.All(a => a.ToString().StartsWith("10.10.14.") && !a.Equals(network.Address)), "Discovery uses the actual subnet and excludes its own, network and broadcast addresses");
        var small = new LanInterface(IPAddress.Parse("192.168.2.130"), 26);
        check(small.Broadcast!.ToString() == "192.168.2.191" && small.ProbeAddresses().Count() == 61, "Discovery respects non-/24 subnet boundaries");
        var large = new LanInterface(IPAddress.Parse("10.20.30.40"), 8);
        check(large.ProbeAddresses().Count() <= 1023 && large.ProbeAddresses().Any(a => a.ToString() == "10.20.30.41"), "Large corporate subnets have a bounded local search window");
        check(new LanInterface(IPAddress.Parse("10.0.0.10"), 31).ProbeAddresses().Single().ToString() == "10.0.0.11" && !new LanInterface(IPAddress.Parse("10.0.0.10"), 32).ProbeAddresses().Any(), "Point-to-point and host-only networks never leak into adjacent subnets");
        check(LanDiscovery.Targets([network, network, new(IPAddress.Parse("10.10.14.109"), 24)]).Length == 252, "Multiple interfaces deduplicate targets and exclude every local address");
        var routed = LanDiscovery.ParseRanges("10.10.14.0/24, 10.10.15.0/24;10.10.15.1", [network.Address]);
        check(routed.Length == 507 && routed.Contains(IPAddress.Parse("10.10.15.254")) && !routed.Contains(network.Address), "Explicit ranges scan across routed subnets, deduplicate and exclude local addresses");
        check(LanDiscovery.ParseRanges("172.16.0.1/16", []).Length == 65534, "Explicit /16 scans include the entire requested network without a silent local-window cap");
        check(LanDiscovery.ParseRanges("10.10", []).SequenceEqual(LanDiscovery.ParseRanges("10.10.0.0/16", [])), "Two-octet prefix expands to every address in the requested /16");
        check(LanDiscovery.ParseRanges("10.10.14", []).SequenceEqual(LanDiscovery.ParseRanges("10.10.14.0/24", [])), "Three-octet prefix expands to the requested /24");
        foreach (var invalid in new[] { "10.0.0.0/8", "8.8.8.8", "::1", "10.0.0.0/16,10.1.0.0/16", "10.1.1.1/nope", "10.10/16" })
        {
            bool rejected = false; try { LanDiscovery.ParseRanges(invalid, []); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Discovery rejects invalid, public or oversized range: " + invalid);
        }

        using var aId = new Identity(Path.Combine(root, "discovery-a"), "An dò máy");
        using var bId = new Identity(Path.Combine(root, "discovery-b"), "Bình dò máy");
        using var cId = new Identity(Path.Combine(root, "discovery-c"), "Chi dò máy");
        using var aStore = new LocalStore(Path.Combine(root, "discovery-a"));
        using var bStore = new LocalStore(Path.Combine(root, "discovery-b"));
        using var cStore = new LocalStore(Path.Combine(root, "discovery-c"));
        await using var a = new PeerNode(aId, aStore);
        await using var b = new PeerNode(bId, bStore);
        await using var c = new PeerNode(cId, cStore);
        await a.StartAsync(0, false, true); await b.StartAsync(0, false, true); await c.StartAsync(0, false, true);
        var notices = new List<string>(); a.Notice += notices.Add;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var count = await a.DiscoverTargetsAsync([new(IPAddress.Loopback, b.Port)], [], timeout.Token);
        check(count == 1 && a.IsConnected(bId.Id) && !a.FindPeer(bId.Id)!.Trusted, "Direct discovery finds a legacy peer with UDP disabled and never grants trust");
        var denied = false;
        try { await a.SendTextAsync(bId.Id, "not paired"); } catch (InvalidOperationException) { denied = true; }
        check(denied, "A peer found through direct probing still requires pairing before chat");

        using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)responder.Client.LocalEndPoint!;
        var respond = Task.Run(async () => await c.HandleDiscoveryPacketAsync(await responder.ReceiveAsync(timeout.Token), responder, timeout.Token));
        count = await a.DiscoverTargetsAsync([], [endpoint], timeout.Token); await respond;
        check(count == 2 && a.FindPeer(cId.Id)?.Port == c.Port && !a.IsConnected(cId.Id), "Unicast UDP discovers the advertised dynamic TCP port without multicast or an implicit connection");
        check(!a.FindPeer(cId.Id)!.Trusted && a.FindPeer(cId.Id)!.Address == "127.0.0.1", "Unicast metadata remains untrusted and uses the packet source address");
        var progress = new ProbeProgress();
        await a.DiscoverTargetsAsync([new(IPAddress.Loopback, b.Port), new(IPAddress.Parse("127.0.1.1"), b.Port)], [], timeout.Token, progress);
        check(progress.Values.Select(p => p.Range).Distinct().SequenceEqual(new[] { "127.0.0.0/24", "127.0.1.0/24" }) && progress.Values.Last().Completed == 2, "Discovery scans each /24 sequentially and reports the current range and overall completion");

        using var input = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var inputEndpoint = (IPEndPoint)input.Client.LocalEndPoint!;
        foreach (var json in new[] { "{", "{\"Type\":\"lantern-v1\",\"Id\":null}", Codec.Encode(new WireMessage { Type = "lantern-v1", Id = new string('a', 64), Name = "bad\nname", Port = 45853 }) })
            await a.HandleDiscoveryPacketAsync(new UdpReceiveResult(Encoding.UTF8.GetBytes(json), inputEndpoint), input, timeout.Token);
        check(a.Peers.Count == 2, "Malformed discovery packets cannot create peers or stop reception");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool cancelledCorrectly = false;
        try { await a.DiscoverTargetsAsync([new(IPAddress.Loopback, b.Port)], [], cancelled.Token); } catch (OperationCanceledException) { cancelledCorrectly = true; }
        check(cancelledCorrectly && notices.Count == 0 && await a.DiscoverAsync() == 0, "Discovery cancels cleanly, stays quiet and honors loopback-only test mode");
    }
    private sealed class ProbeProgress : IProgress<DiscoveryProgress>
    {
        public List<DiscoveryProgress> Values { get; } = [];
        public void Report(DiscoveryProgress value) { lock (Values) Values.Add(value); }
    }
}
