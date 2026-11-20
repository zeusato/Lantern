using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Lantern.Core;

internal sealed record LanInterface(IPAddress Address, int PrefixLength)
{
    private uint Host => BinaryPrimitives.ReadUInt32BigEndian(Address.GetAddressBytes());
    private uint Mask => PrefixLength == 0 ? 0 : uint.MaxValue << (32 - PrefixLength);
    private static IPAddress FromNumber(uint number)
    {
        byte[] bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, number); return new IPAddress(bytes);
    }
    public IPAddress? Broadcast => PrefixLength is > 0 and < 31 ? FromNumber(Host | ~Mask) : null;
    public IEnumerable<IPAddress> ProbeAddresses()
    {
        // Never sweep an entire corporate /8 or /16. Search the local /22 window
        // inside larger subnets; smaller subnets retain their actual boundaries.
        if (PrefixLength is < 1 or > 32) yield break;
        var scanMask = uint.MaxValue << (32 - Math.Max(PrefixLength, 22));
        var first = Host & scanMask; var last = Host | ~scanMask;
        for (ulong candidate = first; candidate <= last; candidate++)
        {
            var value = (uint)candidate;
            if (value == Host || PrefixLength < 31 && (value == (Host & Mask) || value == (Host | ~Mask))) continue;
            var address = FromNumber(value);
            if (PeerNode.IsLanAddress(address) && !IPAddress.IsLoopback(address)) yield return address;
        }
    }
}

internal static class LanDiscovery
{
    public static IPAddress[] ParseRanges(string ranges, IEnumerable<IPAddress> localAddresses)
    {
        var local = localAddresses.ToHashSet(); var result = new HashSet<IPAddress>();
        foreach (var part in ranges.Split([' ', ',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('/');
            if (pieces.Length == 1)
            {
                var octets = part.TrimEnd('.').Split('.');
                if (octets.Length is 2 or 3 && octets.All(s => byte.TryParse(s, out _)))
                    pieces = [string.Join(".", octets.Concat(Enumerable.Repeat("0", 4 - octets.Length))), (octets.Length * 8).ToString()];
            }
            if (pieces.Length > 2 || pieces[0].Split('.').Length != 4 || !IPAddress.TryParse(pieces[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
                !int.TryParse(pieces.Length == 1 ? "32" : pieces[1], out var prefix) || prefix is < 16 or > 32)
                throw new ArgumentException("Nhập tiền tố như 10.10 hoặc 10.10.14, IPv4 đầy đủ hoặc CIDR từ /16 đến /32. Tối đa 65.536 địa chỉ mỗi lượt.");
            var host = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes()); var mask = uint.MaxValue << (32 - prefix);
            ulong first = host & mask, last = host | ~mask;
            if (prefix < 31) { first++; last--; }
            for (var value = first; value <= last; value++)
            {
                byte[] bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value); var ip = new IPAddress(bytes);
                if (!PeerNode.IsLanAddress(ip) || IPAddress.IsLoopback(ip)) throw new ArgumentException("Chỉ quét địa chỉ IPv4 trong mạng LAN riêng.");
                if (!local.Contains(ip)) result.Add(ip);
                if (result.Count > 65536) throw new ArgumentException("Tối đa 65.536 địa chỉ mỗi lượt quét; hãy chia nhỏ dải mạng.");
            }
        }
        if (result.Count == 0) throw new ArgumentException("Dải quét chưa có địa chỉ máy khác.");
        return result.ToArray();
    }
    public static LanInterface[] Interfaces()
    {
        var result = new List<LanInterface>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                try
                {
                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork && PeerNode.IsLanAddress(address.Address) && !IPAddress.IsLoopback(address.Address))
                            result.Add(new LanInterface(address.Address, address.PrefixLength));
                }
                catch (NetworkInformationException) { }
            }
        }
        catch (NetworkInformationException) { }
        return result.Distinct().ToArray();
    }

    public static IPAddress[] Targets(IEnumerable<LanInterface> interfaces)
    {
        var networks = interfaces.ToArray(); var local = networks.Select(n => n.Address).ToHashSet();
        return networks.SelectMany(n => n.ProbeAddresses()).Where(a => !local.Contains(a)).Distinct().Take(4096).ToArray();
    }
}
