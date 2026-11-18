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
