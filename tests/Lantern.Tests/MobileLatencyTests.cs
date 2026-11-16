using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Lantern.Core;

internal static class MobileLatencyTests
{
    public static async Task Run(PeerNode node, PeerNode other, MobileServer server, Identity phone, Action<bool,string> check, RelayConfiguration? relay)
    {
        using var invitation = JsonDocument.Parse(await server.CreateInvitationAsync()); var qr = invitation.RootElement;
        using var tcp = new TcpClient { NoDelay = true }; RelayApi? api = null; HttpRelayStream? tunnel = null; Stream transport;
        if (relay is null) { await tcp.ConnectAsync("127.0.0.1", server.Port); transport = tcp.GetStream(); }
        else { var r = qr.GetProperty("relay"); var route = new MobileRoute { Id = r.GetProperty("id").GetString()!, ClientKey = r.GetProperty("key").GetString()! }; api = new RelayApi(relay); string session = Guid.NewGuid().ToString("N"); await api.Call("v1/routes/" + route.Id + "/open", route.ClientKey, new { session }, CancellationToken.None); tunnel = new HttpRelayStream(api, route, session, false, CancellationToken.None); transport = tunnel; }
        using var ssl = new SslStream(transport, true, (_, cert, _, _) => cert is not null && Identity.PeerId(new X509Certificate2(cert)) == node.Identity.Id);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var pending = new ConcurrentDictionary<string,TaskCompletionSource<MobilePacket>>(); var pushes = Channel.CreateUnbounded<MobilePacket>(); Task? reader = null;
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "lantern.local", ClientCertificates = new X509CertificateCollection { phone.Certificate }, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, stop.Token);
            await MobileProtocol.WriteAsync(ssl, new() { Type = "hello", Name = "Latency QA", Nonce = qr.GetProperty("nonce").GetString()!, Capabilities = ["push-v1"] }, stop.Token);
            var welcome = await MobileProtocol.ReadAsync(ssl, stop.Token); check(welcome.Capabilities.Contains("push-v1"), "PC negotiates realtime push with capable phones");
            reader = Task.Run(async () => { try { while (!stop.IsCancellationRequested) { var packet = await MobileProtocol.ReadAsync(ssl, stop.Token); if (packet.Id.Length == 0) await pushes.Writer.WriteAsync(packet, stop.Token); else if (pending.TryGetValue(packet.Id, out var completion)) completion.TrySetResult(packet); } } catch (Exception e) { foreach (var p in pending.Values) p.TrySetException(e); pushes.Writer.TryComplete(e); } });
            async Task<MobilePacket> Call(MobilePacket packet) { if (packet.Id.Length == 0) packet.Id = Guid.NewGuid().ToString("N"); var done = new TaskCompletionSource<MobilePacket>(TaskCreationOptions.RunContinuationsAsynchronously); pending[packet.Id] = done; try { await MobileProtocol.WriteAsync(ssl, packet, stop.Token); return await done.Task.WaitAsync(stop.Token); } finally { pending.TryRemove(packet.Id, out _); } }
            async Task<MobilePacket> Push(Func<MobilePacket,bool> match) { while (true) { var p = await pushes.Reader.ReadAsync(stop.Token); if (match(p)) return p; } }
            await Call(new() { Type = "unlock", Conversation = other.Identity.Id, Password = "conversation-secret" });
            for (int i = 0; i < 30; i++) await node.SendTextAsync(other.Identity.Id, "Synthetic history " + i);
            var timer = Stopwatch.StartNew(); var history = await Call(new() { Type = "sync", Conversation = other.Identity.Id });
            Console.WriteLine($"LATENCY { (relay is null ? "LAN" : "RELAY") } history30_ms={timer.ElapsedMilliseconds}");
            check(history.Messages.Count >= 30, "A single sync supplies contacts and thirty-message history");
            for (int i = 0; i < 5; i++)
            {
                var request = new MobilePacket { Type = "send", Id = Guid.NewGuid().ToString("N"), Conversation = other.Identity.Id, Text = "Latency probe " + i };
                string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phone.Id + request.Id)))[..32].ToLowerInvariant();
                timer.Restart(); var response = Call(request);
                while (node.Store.Message(id) is null) await Task.Delay(5, stop.Token); long toPc = timer.ElapsedMilliseconds;
                var ack = await response; long ackMs = timer.ElapsedMilliseconds;
                check(ack.MessageId == id && ack.Messages.Single().Id == id, "Send acknowledgement carries the committed message without a history reload");
                timer.Restart(); var incoming = await other.SendTextAsync(node.Identity.Id, "Incoming probe " + i);
                await Push(p => p.Messages.Any(m => m.Id == incoming.Id)); long receive = timer.ElapsedMilliseconds;
                Console.WriteLine($"LATENCY {(relay is null ? "LAN" : "RELAY")} sample={i+1} phone_to_pc_ms={toPc} ack_ms={ackMs} lan_to_phone_push_ms={receive}");
                if (relay is null) check(toPc < 2000 && receive < 2000, "Realtime LAN sending and receiving stay below two seconds");
            }
            server.LockConversation(other.Identity.Id);
            var locked = await Push(p => p.Locked && p.Conversation == other.Identity.Id);
            check(locked.Messages.Count == 0, "PC manual lock pushes an empty protected view immediately");
        }
        finally { stop.Cancel(); ssl.Close(); if (reader is not null) try { await reader; } catch (Exception) { } if (tunnel is not null) await tunnel.DisposeAsync(); api?.Dispose(); await server.RevokeAsync(phone.Id); }
    }
}
