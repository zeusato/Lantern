// Loopback-only synthetic PWA fixture. Not referenced or packaged by the app.
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Lantern.Core;
AppDomain.CurrentDomain.FirstChanceException += (_, e) => { if (e.Exception is System.Security.Authentication.AuthenticationException or System.Security.Cryptography.CryptographicException or InvalidDataException) Console.Error.WriteLine("Fixture diagnostic: " + e.Exception.GetType().Name + ": " + e.Exception.Message); };
var root = args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "Lantern-web-qa-" + Guid.NewGuid().ToString("N"));
using var vault = WorkspaceVault.Create(root, "web-fixture-password");
using var identity = new Identity(root, "Quyết · PC thử nghiệm", vault);
using var store = new LocalStore(root, vault);
await using var node = new PeerNode(identity, store);
var peerPath = Path.Combine(root, "peer"); using var peerIdentity = new Identity(peerPath, "Minh Anh"); using var peerStore = new LocalStore(peerPath);
await using var peer = new PeerNode(peerIdentity, peerStore);
await node.StartAsync(0, false, true); await peer.StartAsync(0, false, true);
await node.ConnectAsync("127.0.0.1", peer.Port, peerIdentity.Id);
for (int i = 0; i < 100 && (!node.IsConnected(peerIdentity.Id) || !peer.IsConnected(identity.Id)); i++) await Task.Delay(50);
await node.TrustAsync(peerIdentity.Id); await peer.TrustAsync(identity.Id);
for (int i = 0; i < 30; i++) store.SaveMessage(new ChatMessage { Id = i.ToString("x32"), ConversationId = peerIdentity.Id, SenderId = i % 6 < 3 ? peerIdentity.Id : identity.Id, SenderName = i % 6 < 3 ? "Minh Anh" : identity.Name, Outgoing = i % 6 >= 3, Text = i switch { 28 => "Chiều nay mình gửi bản thiết kế mới nhé 🙂", 29 => "Ừ, gửi mình xem với. Cảm ơn Anh!", _ => new[] { "Mình vừa xem lại bản thiết kế rồi.", "Phần khung chat gọn hơn nhiều 👍", "Màu này nhìn dễ chịu thật.", "Mình đang chỉnh nốt phần ảnh.", "Gửi tin trên điện thoại cũng phải thật mượt.", "Nhất trí, chiều mình thử tiếp nhé." }[i % 6] }, Status = "read", Time = DateTimeOffset.Now.AddMinutes(-30 + i) });
var lockedId = new string('b', 64); store.SavePeer(new Peer { Id = lockedId, Name = "Hội thoại riêng tư", Trusted = true, Address = "127.0.0.1", Port = 1 });
// Reloading peers is unnecessary for the real connected conversation; use that one for lock tests.
await using var server = new MobileServer(node, vault); server.ApprovePairing = _ => Task.FromResult(true);
var desktop = new FixtureDesktop(); server.PcControl = new PcControl(server.Registry, desktop, () => true);
var sharedPath = Path.Combine(root, "pc-shared"); Directory.CreateDirectory(sharedPath);
File.WriteAllText(Path.Combine(sharedPath, "document.txt"), "Lantern PC file fixture");
var sharedRoot = new PcRoot(Guid.NewGuid().ToString("N"), "Documents", sharedPath);
var route = new MobileRoute { Transport = "web-v1" }; var nonce = MobileRoute.Secret(); var sessionLife = new CancellationTokenSource();
JsonElement? publicQr = null;
if (args.Contains("--public-relay"))
{
 await server.StartAsync(0, true);
 using var generated = JsonDocument.Parse(await server.CreateInvitationAsync(web: true)); publicQr = generated.RootElement.Clone();
 var invitation = (MobileInvitation)typeof(MobileServer).GetField("invitation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
 route = invitation.Route!; nonce = invitation.Nonce;
}
else typeof(MobileServer).GetField("invitation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(server, new MobileInvitation(nonce, DateTimeOffset.UtcNow.AddHours(2), route));
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
var metadata = new { port, conversation = peerIdentity.Id, qr = (object?)publicQr ?? new { v = 2, name = identity.Name, pc = identity.Id, nonce, expires = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds(), relay = new { url = "http://localhost:5180/", id = route.Id, key = route.ClientKey } } };
Console.WriteLine(JsonSerializer.Serialize(metadata, MobileProtocol.Json));
var serving = typeof(MobileServer).GetMethod("Serve", BindingFlags.Instance | BindingFlags.NonPublic)!;
while (true)
{
 var tcp = await listener.AcceptTcpClientAsync(); tcp.NoDelay = true;
 _ = Task.Run(async () => { using(tcp) try {
  using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
  var open = await MobileProtocol.ReadAsync(tcp.GetStream(), timeout.Token);
  if(open.Type == "fixture-pc-grant") { foreach(var device in server.Registry.Snapshot().Devices) server.Registry.SetAccess(device.Id, new() { Power = true, Ai = true, Roots = [sharedRoot] }); await MobileProtocol.WriteAsync(tcp.GetStream(), new MobilePacket { Type = "ok" }, timeout.Token); return; }
  if(open.Type == "fixture-renew") { typeof(MobileServer).GetField("invitation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(server, new MobileInvitation(nonce, DateTimeOffset.UtcNow.AddHours(2), route)); await MobileProtocol.WriteAsync(tcp.GetStream(), new MobilePacket { Type = "ok" }, timeout.Token); return; }
  if(open.Type == "fixture-lock") { vault.SetConversationPassword(peerIdentity.Id, "conversation-secret"); server.LockConversation(peerIdentity.Id); await MobileProtocol.WriteAsync(tcp.GetStream(), new MobilePacket { Type = "ok" }, timeout.Token); return; }
  if(open.Type == "fixture-message") { await peer.SendTextAsync(identity.Id, open.Text); await MobileProtocol.WriteAsync(tcp.GetStream(), new MobilePacket { Type = "ok" }, timeout.Token); return; }
  if(open.Type == "fixture-revoke") { foreach(var device in server.Registry.Snapshot().Devices) await server.RevokeAsync(device.Id); server.CancelInvitation(); await MobileProtocol.WriteAsync(tcp.GetStream(), new MobilePacket { Type = "ok" }, timeout.Token); return; }
  if(open.Type != "web-open" || !Guid.TryParseExact(open.Id, "N", out _)) return;
  await (Task)serving.Invoke(server, [tcp.GetStream(), route.Id, open.Id])!;
 } catch(Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); } });
}
sealed class FixtureDesktop : IPcDesktop
{
 private int calls;
 public Task<List<PcTarget>> InspectAsync(CancellationToken ct) => Task.FromResult(new List<PcTarget> { new("fixture-ai", "Synthetic AI", "Test chat", "idle", "fixture-version", "Ready") });
 public Task<string> ExecuteAsync(PcPacket request, Action authorize, CancellationToken ct) { authorize(); return Task.FromResult("fixture call " + Interlocked.Increment(ref calls)); }
}
