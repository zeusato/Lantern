using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Lantern.Core;

public sealed partial class PeerNode
{
    private bool discoveryEnabled;
    private readonly SemaphoreSlim announcementGate = new(1, 1);
    private readonly SemaphoreSlim discoverySearch = new(1, 1);
    private readonly HashSet<IPAddress> joinedInterfaces = [];
    private readonly Dictionary<IPAddress, DateTimeOffset> probeReplies = [];
    private byte[] DiscoveryPacket(string type = "lantern-v1") => Encoding.UTF8.GetBytes(Codec.Encode(new WireMessage { Type = type, Id = Identity.Id, Name = Identity.Name, Port = Port }));

    private WireMessage? ReadDiscoveryPacket(UdpReceiveResult packet)
    {
        if (packet.Buffer.Length > 1024 || !IsLanAddress(packet.RemoteEndPoint.Address)) return null;
        try
        {
            var msg = Codec.Decode<WireMessage>(Encoding.UTF8.GetString(packet.Buffer)); Protocol.Validate(msg);
            return msg.Id != Identity.Id && msg.Id.Length == 64 && msg.Id.All(Uri.IsHexDigit) &&
                msg.Name.Length is >= 1 and <= 80 && !string.IsNullOrWhiteSpace(msg.Name) && !msg.Name.Any(char.IsControl) &&
                msg.Port is >= 1 and <= 65535 ? msg : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException) { return null; }
    }

    internal async Task HandleDiscoveryPacketAsync(UdpReceiveResult packet, UdpClient receiver, CancellationToken ct)
    {
        var msg = ReadDiscoveryPacket(packet);
        if (msg is null || peers.TryGetValue(msg.Id, out var known) && known.Blocked) return;
        if (msg.Type == "lantern-probe")
        {
            // Reply to the request's source port; do not broadcast replies or echo
            // announcements. Bound unsolicited UDP replies by source and time.
            var now = DateTimeOffset.UtcNow;
            lock (probeReplies)
            {
                foreach (var stale in probeReplies.Where(p => now - p.Value >= TimeSpan.FromSeconds(2)).Select(p => p.Key).ToArray()) probeReplies.Remove(stale);
                if (probeReplies.ContainsKey(packet.RemoteEndPoint.Address) || probeReplies.Count >= 256) return;
                probeReplies[packet.RemoteEndPoint.Address] = now;
            }
            await SendDiscoveryPacket(receiver, DiscoveryPacket(), packet.RemoteEndPoint, ct);
        }
        else if (msg.Type == "lantern-v1") ObserveDiscovery(msg, packet.RemoteEndPoint.Address);
    }

    private void ObserveDiscovery(WireMessage msg, IPAddress address)
    {
        if (peers.Count >= 256 && !peers.ContainsKey(msg.Id)) return;
        var peer = peers.GetOrAdd(msg.Id, id => new Peer { Id = id, Name = msg.Name });
        if (peer.Blocked || peer.Connected) return;
        peer.Address = address.ToString(); peer.Port = msg.Port; peer.Seen = DateTimeOffset.UtcNow;
        Changed?.Invoke();
        if (peer.Trusted) Track(QuietConnect(peer));
    }

    private async Task DiscoveryLoop()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                try { await HandleDiscoveryPacketAsync(await discovery!.ReceiveAsync(lifetime.Token), discovery, lifetime.Token); }
                // Windows can surface an ICMP port-unreachable as a UDP receive
                // error. One failed reply must not stop discovery permanently.
                catch (SocketException) { await Task.Delay(100, lifetime.Token); }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
    }

    private static async Task SendDiscoveryPacket(UdpClient udp, byte[] packet, IPEndPoint target, CancellationToken ct)
    {
        try { await udp.SendAsync(packet, target, ct); }
        catch (SocketException) { }
    }

    public async Task AnnounceAsync()
    {
        if (discovery is null) return;
        try
        {
            await announcementGate.WaitAsync(lifetime.Token);
            try
            {
                var networks = LanDiscovery.Interfaces();
                var current = networks.Select(n => n.Address).ToHashSet();
                foreach (var removed in joinedInterfaces.Where(a => !current.Contains(a)).ToArray())
                {
                    try { discovery.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, new MulticastOption(IPAddress.Parse(MulticastAddress), removed)); } catch (SocketException) { }
                    joinedInterfaces.Remove(removed);
                }
                var packet = DiscoveryPacket();
                foreach (var network in networks)
                {
                    if (!joinedInterfaces.Contains(network.Address))
                    {
                        try { discovery.JoinMulticastGroup(IPAddress.Parse(MulticastAddress), network.Address); joinedInterfaces.Add(network.Address); } catch (SocketException) { }
                    }
                    try
                    {
                        using var udp = new UdpClient(new IPEndPoint(network.Address, 0)); udp.EnableBroadcast = true;
                        try
                        {
                            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, network.Address.GetAddressBytes());
                            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                            await SendDiscoveryPacket(udp, packet, new IPEndPoint(IPAddress.Parse(MulticastAddress), DiscoveryPort), lifetime.Token);
                        }
                        catch (SocketException) { }
                        if (network.Broadcast is { } broadcast) await SendDiscoveryPacket(udp, packet, new IPEndPoint(broadcast, DiscoveryPort), lifetime.Token);
                        await SendDiscoveryPacket(udp, packet, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort), lifetime.Token);
                    }
                    catch (SocketException) { }
                }
            }
            finally { announcementGate.Release(); }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
    }

    public static string[] DiscoveryRanges() => LanDiscovery.Interfaces().Select(n => n.Address.GetAddressBytes()[0] == 10 ? string.Join(".", n.Address.GetAddressBytes().Take(2)) : $"{n.Address}/{Math.Max(n.PrefixLength, 22)}").Distinct().ToArray();
    public Task<int> DiscoverAsync(CancellationToken ct = default) => DiscoverAsync(null, null, ct);
    public async Task<int> DiscoverAsync(string? ranges, IProgress<DiscoveryProgress>? progress, CancellationToken ct = default)
    {
        if (!discoveryEnabled) return 0;
        await AnnounceAsync();
        var targets = string.IsNullOrWhiteSpace(ranges) ? LanDiscovery.Targets(LanDiscovery.Interfaces()) : LanDiscovery.ParseRanges(ranges, LanDiscovery.Interfaces().Select(n => n.Address));
        // Search the machine's current /24 first, then visit the other /24s in order.
        var nearby = LanDiscovery.Interfaces().Select(n => SubnetKey(n.Address)).ToHashSet();
        targets = targets.OrderBy(a => nearby.Contains(SubnetKey(a)) ? 0 : 1).ToArray();
        var tcp = targets.Select(a => new IPEndPoint(a, 45853)).Concat(Peers.Where(p => !p.Blocked && p.Port > 0 && IPAddress.TryParse(p.Address, out var ip) && IsLanAddress(ip)).Select(p => new IPEndPoint(IPAddress.Parse(p.Address), p.Port))).Distinct().ToArray();
        var search = DiscoverTargetsAsync(tcp, targets.Select(a => new IPEndPoint(a, DiscoveryPort)).ToArray(), ct, progress);
        Track(search);
        return await search;
    }

    internal async Task<int> DiscoverTargetsAsync(IReadOnlyList<IPEndPoint> tcpTargets, IReadOnlyList<IPEndPoint> udpTargets, CancellationToken ct, IProgress<DiscoveryProgress>? progress = null)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, ct);
        await discoverySearch.WaitAsync(cancel.Token);
        try
        {
            var started = DateTimeOffset.UtcNow;
            int completed = 0;
            var tcpGroups = tcpTargets.GroupBy(t => SubnetKey(t.Address)).ToDictionary(g => g.Key, g => g.ToArray());
            var udpGroups = udpTargets.GroupBy(t => SubnetKey(t.Address)).ToDictionary(g => g.Key, g => g.ToArray());
            foreach (var subnet in tcpGroups.Keys.Concat(udpGroups.Keys).Distinct())
            {
                cancel.Token.ThrowIfCancellationRequested();
                var range = subnet + ".0/24"; progress?.Report(new(completed, tcpTargets.Count, range));
                var udp = ProbeUdpAsync(udpGroups.GetValueOrDefault(subnet) ?? [], cancel.Token);
                var tcp = Parallel.ForEachAsync(tcpGroups.GetValueOrDefault(subnet) ?? [], new ParallelOptions { MaxDegreeOfParallelism = 64, CancellationToken = cancel.Token }, async (target, token) =>
                {
                    if (!peers.Values.Any(p => p.Address == target.Address.ToString() && p.Port == target.Port && (p.Blocked || p.Connected || p.Seen >= started))) await ProbeTcpAsync(target, token);
                    var done = Interlocked.Increment(ref completed);
                    if (done % 32 == 0 || done == tcpTargets.Count) progress?.Report(new(done, tcpTargets.Count, range));
                });
                await Task.WhenAll(udp, tcp);
            }
            cancel.Token.ThrowIfCancellationRequested();
            return peers.Values.Count(p => !p.Blocked && (p.Connected || p.Seen >= started));
        }
        finally { discoverySearch.Release(); }
    }

    private static string SubnetKey(IPAddress address) => string.Join(".", address.GetAddressBytes().Take(3));

    private async Task ProbeUdpAsync(IReadOnlyList<IPEndPoint> targets, CancellationToken ct)
    {
        if (targets.Count == 0) return;
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            using var receiveCancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var receive = ReceiveProbeRepliesAsync(udp, receiveCancel.Token);
            try
            {
                var packet = DiscoveryPacket("lantern-probe");
                for (int i = 0; i < targets.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await SendDiscoveryPacket(udp, packet, targets[i], ct);
                    if (i % 32 == 31) await Task.Delay(20, ct);
                }
                await Task.Delay(1500, ct);
            }
            finally { await receiveCancel.CancelAsync(); await receive; }
        }
        catch (SocketException) { }
    }

    private async Task ReceiveProbeRepliesAsync(UdpClient udp, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var packet = await udp.ReceiveAsync(ct);
                    if (ReadDiscoveryPacket(packet) is { Type: "lantern-v1" } msg) ObserveDiscovery(msg, packet.RemoteEndPoint.Address);
                }
                catch (SocketException) { await Task.Delay(20, ct); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProbeTcpAsync(IPEndPoint target, CancellationToken ct)
    {
        if (!IsLanAddress(target.Address) || peers.Count >= 256) return;
        var endpoint = target.ToString();
        if (!connecting.TryAdd(endpoint, 0)) return;
        try
        {
            var client = new TcpClient();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(700);
                await client.ConnectAsync(target, timeout.Token);
                await handshakes.WaitAsync(ct);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            { client.Dispose(); return; }
            await Establish(client, null, true, quiet: true, ct);
        }
        finally { connecting.TryRemove(endpoint, out _); }
    }
}
