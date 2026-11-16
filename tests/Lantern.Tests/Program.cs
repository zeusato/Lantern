using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Lantern.Core;

if (args.FirstOrDefault() == "--ui-peer") { await UiPeer.Run(args[1], args[2]); return; }

var root = args.FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "Lantern-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
async Task Until(Func<bool> condition, string name, int milliseconds = 10000)
{
    var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (!condition()) { if (DateTime.UtcNow > end) throw new TimeoutException(name); await Task.Delay(40); }
}
using var aliceIdentity = new Identity(Path.Combine(root, "alice"), "An");
using var bobIdentity = new Identity(Path.Combine(root, "bob"), "Bình");
using var camIdentity = new Identity(Path.Combine(root, "cam"), "Chi");
using var aliceStore = new LocalStore(Path.Combine(root, "alice"));
using var bobStore = new LocalStore(Path.Combine(root, "bob"));
using var camStore = new LocalStore(Path.Combine(root, "cam"));
await using var alice = new PeerNode(aliceIdentity, aliceStore);
await using var bob = new PeerNode(bobIdentity, bobStore);
await using var cam = new PeerNode(camIdentity, camStore);
alice.Notice += s => Console.WriteLine("INFO A: " + s);
bob.Notice += s => Console.WriteLine("INFO B: " + s);
await alice.StartAsync(0, false, true); await bob.StartAsync(0, false, true); await cam.StartAsync(0, false, true);
Check(aliceIdentity.Id.Length == 64 && aliceIdentity.Id != bobIdentity.Id, "Unique public-key identities");
using (var again = new Identity(Path.Combine(root, "alice"), "An")) Check(again.Id == aliceIdentity.Id, "DPAPI identity survives restart");
Check(Identity.VerificationCode(aliceIdentity.Id, bobIdentity.Id) == Identity.VerificationCode(bobIdentity.Id, aliceIdentity.Id), "Both peers derive the same verification code");
await alice.ConnectAsync("127.0.0.1", bob.Port, bobIdentity.Id);
await Until(() => alice.IsConnected(bobIdentity.Id) && bob.IsConnected(aliceIdentity.Id), "TLS connection");
Check(!alice.FindPeer(bobIdentity.Id)!.Trusted, "Discovery/connection never grants trust");
Check(alice.FindPeer(bobIdentity.Id)!.Address == "127.0.0.1", "Mapped IPv4 endpoint normalized for automatic reconnect");
bool rejected = false;
try { await alice.SendTextAsync(bobIdentity.Id, "Blocked before pairing"); } catch (InvalidOperationException) { rejected = true; }
Check(rejected, "Unpaired peer cannot receive chat");
await alice.TrustAsync(bobIdentity.Id); await bob.TrustAsync(aliceIdentity.Id);
await alice.UpdateDisplayNameAsync("  An mới  ");
await Until(() => bob.FindPeer(aliceIdentity.Id)!.Name == "An mới", "Live profile update");
Check(bob.FindPeer(aliceIdentity.Id)!.DisplayName == "An mới", "Own display name updates on an authenticated connection");
bob.SetPeerAlias(aliceIdentity.Id, "  Đồng nghiệp  ");
await alice.UpdateDisplayNameAsync("An tiếp theo");
await Until(() => bob.FindPeer(aliceIdentity.Id)!.Name == "An tiếp theo", "Profile update with alias");
Check(bob.FindPeer(aliceIdentity.Id)!.DisplayName == "Đồng nghiệp" && aliceIdentity.Name == "An tiếp theo", "Local alias survives remote rename and never changes the remote profile");
using (var reopenedPeers = new LocalStore(Path.Combine(root, "bob"))) Check(reopenedPeers.Peers().Single(p => p.Id == aliceIdentity.Id).DisplayName == "Đồng nghiệp", "Alias persists on disk");
bob.SetPeerAlias(aliceIdentity.Id, "");
Check(bob.FindPeer(aliceIdentity.Id)!.DisplayName == "An tiếp theo", "Clearing alias restores latest advertised name");
rejected = false; try { await alice.UpdateDisplayNameAsync(" "); } catch (ArgumentException) { rejected = true; }
Check(rejected && aliceIdentity.Name == "An tiếp theo", "Invalid display name cannot replace profile");
var hello = await alice.SendTextAsync(bobIdentity.Id, "Xin chào Bình 👋 — tiếng Việt có dấu");
await Until(() => bobStore.Message(hello.Id) is not null && aliceStore.Message(hello.Id)?.Status == "delivered", "Message delivery");
Check(bobStore.Message(hello.Id)!.Text == hello.Text, "UTF-8 chat over mutual TLS with durable ACK");
Check(bobStore.UnreadCount() == 1 && aliceStore.UnreadCount() == 0, "Incoming text increments unread, outgoing text does not");
await bob.MarkReadAsync(aliceIdentity.Id);
await Until(() => aliceStore.Message(hello.Id)?.Status == "read", "Read receipt");
Check(true, "Read receipt");
Check(bobStore.UnreadCount() == 0, "Reading a conversation clears unread count");
await alice.TrustAsync(bobIdentity.Id); await Task.Delay(100);
Check(bobStore.Messages().Count(m => m.Id == hello.Id) == 1, "Replay is idempotent");
Check(bobStore.UnreadCount() == 0, "Replay cannot resurrect unread badges");
await bob.ReactAsync(hello.Id, "❤️");
await Until(() => aliceStore.Message(hello.Id)!.Reactions.GetValueOrDefault(bobIdentity.Id) == "❤️", "Direct reaction");
Check(aliceStore.UnreadCount() == 0 && bobStore.UnreadCount() == 0, "Reactions sync without increasing unread message counts");
await bob.ReactAsync(hello.Id, "👍"); await bob.ReactAsync(hello.Id, "");
await Until(() => aliceStore.Message(hello.Id)!.ReactionVersions.GetValueOrDefault(bobIdentity.Id) == 3, "Reaction removal");
Check(aliceStore.Message(hello.Id)!.Reactions[bobIdentity.Id] == "", "Changing and removing own reaction propagates");
var reactionSnapshot = bobStore.Message(hello.Id)!;
bobStore.UpdateMessage(hello.Id, m => { m.Reactions[bobIdentity.Id] = "❤️"; m.ReactionVersions[bobIdentity.Id] = 1; });
await bob.TrustAsync(aliceIdentity.Id); await Task.Delay(100);
Check(aliceStore.Message(hello.Id)!.Reactions[bobIdentity.Id] == "", "Stale replay cannot resurrect a removed reaction");
bobStore.UpdateMessage(hello.Id, m => { m.Reactions = reactionSnapshot.Reactions; m.ReactionVersions = reactionSnapshot.ReactionVersions; });
var path = Path.Combine(root, "payload.bin"); byte[] payload = RandomNumberGenerator.GetBytes(1024 * 1024 + 123); await File.WriteAllBytesAsync(path, payload);
var offer = await alice.OfferFileAsync(bobIdentity.Id, path);
await Until(() => bobStore.Message(offer.Id) is not null, "File offer");
var received = bobStore.Message(offer.Id)!;
Check(!File.Exists(received.FilePath), "File requires explicit acceptance");
Check(bobStore.UnreadCount() == 1, "File offer counts as an unread message");
await bob.MarkReadAsync(aliceIdentity.Id);
Check(bobStore.UnreadCount() == 0 && bobStore.Message(offer.Id)!.Status == "offered", "Reading an attachment preserves its transfer state");
await File.WriteAllBytesAsync(received.FilePath + ".part", payload[..131072]);
await bob.AcceptFileAsync(offer.Id);
await Until(() => bobStore.Message(offer.Id)?.Status == "received" && aliceStore.Message(offer.Id)?.Status == "delivered", "Resumable file", 20000);
Check((await File.ReadAllBytesAsync(received.FilePath)).SequenceEqual(payload), "Chunk transfer resumes from partial file and verifies SHA-256");
Check(bobStore.UnreadCount() == 0, "Transfer completion does not make an already seen attachment unread");
var empty = Path.Combine(root, "empty.txt"); await File.WriteAllTextAsync(empty, "");
var zero = await alice.OfferFileAsync(bobIdentity.Id, empty); await Until(() => bobStore.Message(zero.Id) is not null, "Zero file offer"); await bob.AcceptFileAsync(zero.Id);
await Until(() => bobStore.Message(zero.Id)?.Status == "received", "Zero file transfer"); Check(true, "Zero-byte attachment");
var corrupt = await alice.OfferFileAsync(bobIdentity.Id, path); await Until(() => bobStore.Message(corrupt.Id) is not null, "Corrupt offer");
await File.WriteAllBytesAsync(bobStore.Message(corrupt.Id)!.FilePath + ".part", new byte[131072]); await bob.AcceptFileAsync(corrupt.Id);
await Until(() => bobStore.Message(corrupt.Id)?.Status == "error", "Hash mismatch"); Check(!File.Exists(bobStore.Message(corrupt.Id)!.FilePath), "Corrupt partial content rejected");
await bob.AcceptFileAsync(corrupt.Id); await Until(() => bobStore.Message(corrupt.Id)?.Status == "received", "Retry corrupt transfer"); Check(true, "Retry after integrity failure succeeds");
var decline = await alice.OfferFileAsync(bobIdentity.Id, empty); await Until(() => bobStore.Message(decline.Id) is not null, "Decline offer"); await bob.DeclineFileAsync(decline.Id);
await Until(() => aliceStore.Message(decline.Id)?.Status == "declined", "Decline ack"); Check(true, "Decline attachment propagates to sender");
await alice.ConnectAsync("127.0.0.1", cam.Port, camIdentity.Id); await bob.ConnectAsync("127.0.0.1", cam.Port, camIdentity.Id);
await Until(() => cam.IsConnected(aliceIdentity.Id) && cam.IsConnected(bobIdentity.Id), "Third peer");
await alice.TrustAsync(camIdentity.Id); await cam.TrustAsync(aliceIdentity.Id); await bob.TrustAsync(camIdentity.Id); await cam.TrustAsync(bobIdentity.Id);
var group = await alice.CreateGroupAsync("Nhóm thiết kế", [bobIdentity.Id, camIdentity.Id]);
await Until(() => bobStore.Groups().Count == 1 && camStore.Groups().Count == 1, "Group invitation");
var groupText = await bob.SendTextAsync(group.Id, "Cả nhóm đã nhận được chưa?");
await Until(() => aliceStore.Message(groupText.Id) is not null && camStore.Message(groupText.Id) is not null, "Group delivery");
Check(aliceStore.Message(groupText.Id)!.ConversationId == group.Id, "Three-peer group membership and direct fan-out");
Check(aliceStore.UnreadCount() == 1 && camStore.UnreadCount() == 1, "Group messages contribute once to each recipient's unread count");
await cam.ReactAsync(groupText.Id, "❤️"); await alice.ReactAsync(groupText.Id, "👍");
await Until(() => bobStore.Message(groupText.Id)!.Reactions.Count == 2, "Group reactions");
Check(bobStore.Message(groupText.Id)!.Reactions[camIdentity.Id] == "❤️", "Group reactions preserve each authenticated actor");
var quote = await alice.SendTextAsync(group.Id, "Em nhận rồi", groupText.Id);
await Until(() => camStore.Message(quote.Id) is not null, "Quoted message");
Check(camStore.Message(quote.Id)!.ReplyToId == groupText.Id, "Quoted reply references the original message on remote peers");
rejected = false; try { await alice.SendTextAsync(bobIdentity.Id, "Wrong conversation", groupText.Id); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Cross-conversation quoting is rejected");
Check(Protocol.SafeFileName("../../outside.exe") == "outside.exe" && Protocol.SafeFileName("..\\..\\secret.txt") == "secret.txt", "Path traversal filenames sanitized");
Check(!PeerNode.IsLanAddress(IPAddress.Parse("8.8.8.8")), "Public Internet targets rejected");
rejected = false; try { Protocol.Validate(Codec.Decode<WireMessage>("{\"Type\":\"lantern-v1\",\"Id\":null}")); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Null fields rejected before discovery/handshake processing");
await using (var malformed = new MemoryStream(new byte[] { 0x7f, 0xff, 0xff, 0xff }))
{
    rejected = false; try { await Protocol.ReadAsync(malformed, CancellationToken.None); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Oversized network frame rejected before allocation");
}
// A discovery packet claiming another ID must not bypass certificate pinning.
using var attackerIdentity = new Identity(Path.Combine(root, "attacker"), "Fake Bình");
using var attackerStore = new LocalStore(Path.Combine(root, "attacker"));
await using var attacker = new PeerNode(attackerIdentity, attackerStore); await attacker.StartAsync(0, false, true);
await alice.ConnectAsync("127.0.0.1", attacker.Port, bobIdentity.Id);
Check(!alice.IsConnected(attackerIdentity.Id), "Pinned peer ID rejects impostor certificate");
var keyFile = Path.Combine(root, "backup.lanternkey"); aliceIdentity.Export(keyFile, "correct horse battery staple");
var restored = Path.Combine(root, "restored"); Identity.Import(keyFile, "correct horse battery staple", restored);
using (var restoredIdentity = new Identity(restored, "An")) Check(restoredIdentity.Id == aliceIdentity.Id, "Password-protected identity export/import");
rejected = false; try { Identity.Import(keyFile, "wrong password", Path.Combine(root, "invalid")); } catch (CryptographicException) { rejected = true; } Check(rejected, "Wrong backup password rejected");
// Test an offline receiver with a persisted outbox, then its changed listening port.
using var offlineIdentity = new Identity(Path.Combine(root, "offline"), "Dũng");
using var offlineStore = new LocalStore(Path.Combine(root, "offline"));
var offline = new PeerNode(offlineIdentity, offlineStore); await offline.StartAsync(0, false, true);
await alice.ConnectAsync("127.0.0.1", offline.Port, offlineIdentity.Id); await Until(() => offline.IsConnected(aliceIdentity.Id), "Offline peer initial connection");
await alice.TrustAsync(offlineIdentity.Id); await offline.TrustAsync(aliceIdentity.Id); await offline.DisposeAsync();
await Until(() => !alice.IsConnected(offlineIdentity.Id), "Offline detection");
var queued = await alice.SendTextAsync(offlineIdentity.Id, "Tin nhắn chờ khi máy tắt"); Check(aliceStore.Message(queued.Id)?.Status == "queued", "Offline text persists in outbox");
await using var onlineAgain = new PeerNode(offlineIdentity, offlineStore); await onlineAgain.StartAsync(0, false, true);
await alice.ConnectAsync("127.0.0.1", onlineAgain.Port, offlineIdentity.Id);
await Until(() => offlineStore.Message(queued.Id) is not null && aliceStore.Message(queued.Id)?.Status == "delivered", "Offline replay");
Check(true, "Reconnect at new endpoint preserves identity and delivers queue");
Check(aliceStore.Messages(bobIdentity.Id, "Xin chào").Count == 1, "Search persisted conversation");
await bob.DeleteConversationHistoryAsync(aliceIdentity.Id);
Check(bobStore.Messages(aliceIdentity.Id).Count == 0 && !File.Exists(received.FilePath) && bobStore.WasDeleted(hello.Id), "Conversation reset removes history/media and retains tombstones");
aliceStore.UpdateMessage(hello.Id, m => { m.Status = "queued"; m.Delivery[bobIdentity.Id] = "queued"; }); await alice.TrustAsync(bobIdentity.Id);
await Until(() => aliceStore.Message(hello.Id)?.Status == "delivered", "Deleted message ACK");
Check(bobStore.Message(hello.Id) is null, "Deleted history is not resurrected by remote replay");
var afterReset = await alice.SendTextAsync(bobIdentity.Id, "Bắt đầu cuộc trò chuyện mới"); await Until(() => bobStore.Message(afterReset.Id) is not null, "New history after reset"); Check(true, "New messages work after local history reset");
var vaultDirectory = Path.Combine(root, "password-vault");
using (var vault = WorkspaceVault.Create(vaultDirectory, "test-only master 2026!"))
{
    Check(WorkspaceVault.IsOwner(vault.Header), "Workspace owner bound to Windows SID and DPAPI proof");
    Check(!ResetPolicy.CanRequestVerification(vault.Header, "XOA") && ResetPolicy.CanRequestVerification(vault.Header, ResetPolicy.Phrase(vault.Header)), "Reset requires exact workspace-specific phrase before Hello");
    var fakeOwner = Codec.Decode<VaultHeader>(Codec.Encode(vault.Header)); fakeOwner.OwnerSid = "S-1-5-21-fake";
    Check(!ResetPolicy.CanRequestVerification(fakeOwner, ResetPolicy.Phrase(fakeOwner)), "Different Windows owner cannot request destructive reset");
    var fakeWorkspace = Codec.Decode<VaultHeader>(Codec.Encode(vault.Header)); fakeWorkspace.WorkspaceId = Guid.NewGuid().ToString("N");
    Check(!WorkspaceVault.IsOwner(fakeWorkspace), "Owner proof cannot be moved to another workspace ID");
    var sealedText = vault.Seal("private sample: 716e4cb5"); Check(!sealedText.Contains("private") && vault.Unseal(sealedText) == "private sample: 716e4cb5", "AES-GCM payload encryption roundtrip");
    var damaged = sealedText[..^5] + (sealedText[^5] == 'A' ? 'B' : 'A') + sealedText[^4..];
    rejected = false; try { vault.Unseal(damaged); } catch (CryptographicException) { rejected = true; } Check(rejected, "Modified ciphertext fails authentication");
    using (var encryptedStore = new LocalStore(vaultDirectory, vault)) encryptedStore.SaveMessage(new ChatMessage { ConversationId = "private", Text = "SECRET-DO-NOT-LEAK-8C426B", SenderId = "test" });
    var rawDb = File.ReadAllBytes(Path.Combine(vaultDirectory, "history.db")); Check(!System.Text.Encoding.UTF8.GetString(rawDb).Contains("SECRET-DO-NOT-LEAK-8C426B"), "Plaintext chat absent from SQLite file");
    using (var lockedStore = new LocalStore(vaultDirectory)) { rejected = false; try { lockedStore.Messages(); } catch (UnauthorizedAccessException) { rejected = true; } Check(rejected, "History cannot be read without unlocked vault"); }
    vault.SetConversationPassword("private", "conversation pass 2026!");
    Check(vault.IsConversationLocked("private") && vault.VerifyConversation("private", "conversation pass 2026!") && !vault.VerifyConversation("private", "wrong"), "Separate conversation password verification");
    using (var opened = WorkspaceVault.Open(vaultDirectory, "test-only master 2026!")) Check(opened.IsConversationLocked("private"), "Conversation locks persist across app restart");
    rejected = false; try { using var wrong = WorkspaceVault.Open(vaultDirectory, "wrong password"); } catch (UnauthorizedAccessException) { rejected = true; } Check(rejected, "Wrong workspace password denied");
    vault.ChangePassword("test-only master 2026!", "new test master 2026!");
    rejected = false; try { using var old = WorkspaceVault.Open(vaultDirectory, "test-only master 2026!"); } catch (UnauthorizedAccessException) { rejected = true; } Check(rejected, "Old password revoked after change");
    using var newVault = WorkspaceVault.Open(vaultDirectory, "new test master 2026!"); using var newStore = new LocalStore(vaultDirectory, newVault);
    Check(newStore.Messages().Single().Text == "SECRET-DO-NOT-LEAK-8C426B", "Password change preserves encrypted history");
    using var protectedIdentity = new Identity(vaultDirectory, "Protected peer", newVault);
    rejected = false; try { using var noPassword = new Identity(vaultDirectory, "No password"); } catch (UnauthorizedAccessException) { rejected = true; } Check(rejected, "Private network key also requires workspace password");
    await using var protectedNode = new PeerNode(protectedIdentity, newStore); await protectedNode.StartAsync(0, false, true);
    await alice.ConnectAsync("127.0.0.1", protectedNode.Port, protectedIdentity.Id); await Until(() => protectedNode.IsConnected(aliceIdentity.Id), "Encrypted peer connection");
    await alice.TrustAsync(protectedIdentity.Id); await protectedNode.TrustAsync(aliceIdentity.Id);
    var intoVault = await alice.SendTextAsync(protectedIdentity.Id, "Mạng thật, lịch sử mã hóa");
    var outOfVault = await protectedNode.SendTextAsync(aliceIdentity.Id, "Đã mở khóa và phản hồi");
    await Until(() => newStore.Message(intoVault.Id) is not null && aliceStore.Message(outOfVault.Id) is not null, "Encrypted store bidirectional delivery");
    Check(newStore.Message(intoVault.Id)!.Text == intoVault.Text, "Encrypted workspace exchanges real peer messages bidirectionally");
    vault.RemoveConversationPassword("private"); Check(!vault.IsConversationLocked("private"), "Conversation lock can be removed after authorization");
}
using var duyIdentity = new Identity(Path.Combine(root, "duy"), "Duy discovery");
using var duyStore = new LocalStore(Path.Combine(root, "duy"));
await using var duy = new PeerNode(duyIdentity, duyStore); await duy.StartAsync(0, false, true);
await bob.ConnectAsync("127.0.0.1", duy.Port, duyIdentity.Id); await Until(() => duy.IsConnected(bobIdentity.Id), "Directory peer connection");
await bob.TrustAsync(duyIdentity.Id); await duy.TrustAsync(bobIdentity.Id);
bob.SetPeerAlias(duyIdentity.Id, "PRIVATE ALIAS NEVER SHARE");
var discovered = await alice.FindContactsAsync("Duy discovery");
Check(discovered.Any(c => c.Id == duyIdentity.Id && c.Name == "Duy discovery") && alice.FindPeer(duyIdentity.Id) is null, "Name search finds a friend's peer without leaking aliases or granting trust");
var contactMessage = await bob.ShareContactAsync(aliceIdentity.Id, duyIdentity.Id);
await Until(() => aliceStore.Message(contactMessage.Id)?.Contact is not null, "Contact sharing");
var card = aliceStore.Message(contactMessage.Id)!.Contact!;
Check(card.Id == duyIdentity.Id && card.Name == "Duy discovery" && card.Port == duy.Port, "Shared contact carries public name, endpoint and pinned identity");
await alice.ConnectAsync(card.Address, card.Port, card.Id); await Until(() => alice.IsConnected(card.Id), "Connect shared card");
Check(!alice.FindPeer(card.Id)!.Trusted, "A shared contact still requires explicit pairing");
rejected = false; try { PeerNode.ValidateContact(new ContactCard { Id = duyIdentity.Id, Name = "Public", Address = "8.8.8.8", Port = 443 }); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "Shared contact rejects Internet endpoints");
await bob.SetPresenceAsync("idle"); await Until(() => alice.FindPeer(bobIdentity.Id)!.Presence == "idle", "Idle presence");
await bob.SetPresenceAsync("dnd"); await Until(() => alice.FindPeer(bobIdentity.Id)!.Presence == "dnd", "DND presence");
Check(true, "Idle and do-not-disturb presence propagate over authenticated connections");
await bob.SetPresenceAsync("online");
alice.FindPeer(bobIdentity.Id)!.ProtocolVersion = 1;
rejected = false; try { await alice.SetDisappearTimerAsync(bobIdentity.Id, 10); } catch (InvalidOperationException) { rejected = true; }
Check(rejected, "Self-destruct refuses peers that do not advertise timer support");
alice.FindPeer(bobIdentity.Id)!.ProtocolVersion = 2;
await alice.SetDisappearTimerAsync(bobIdentity.Id, 10);
await Until(() => bobStore.Policies().Any(p => p.Id == aliceIdentity.Id && p.DisappearSeconds == 10), "Expiry policy sync");
var disappearing = await bob.SendTextAsync(aliceIdentity.Id, "Expires only after reading");
await Until(() => aliceStore.Message(disappearing.Id) is not null, "Expiring message delivery"); await Task.Delay(200);
Check(aliceStore.Message(disappearing.Id)!.ExpiresAt is null && bobStore.Message(disappearing.Id)!.ExpiresAt is null, "Unread self-destruct message has no running timer");
await alice.MarkReadAsync(bobIdentity.Id);
await Until(() => bobStore.Message(disappearing.Id)!.ExpiresAt is not null, "Expiry deadline sync");
Check(aliceStore.Message(disappearing.Id)!.ExpiresAt!.Value.ToUnixTimeMilliseconds() == bobStore.Message(disappearing.Id)!.ExpiresAt!.Value.ToUnixTimeMilliseconds(), "Reading starts the same deadline on both peers");
var disappearingFile = await bob.OfferFileAsync(aliceIdentity.Id, path);
await Until(() => aliceStore.Message(disappearingFile.Id) is not null, "Expiring attachment offer");
await alice.AcceptFileAsync(disappearingFile.Id); await Until(() => aliceStore.Message(disappearingFile.Id)?.Status == "received", "Expiring attachment transfer");
var expiringPath = aliceStore.Message(disappearingFile.Id)!.FilePath;
await alice.MarkReadAsync(bobIdentity.Id);
await alice.SetDisappearTimerAsync(bobIdentity.Id, 0);
await Until(() => bobStore.Policies().Find(p => p.Id == aliceIdentity.Id)?.DisappearSeconds == 0, "Disable expiry sync");
var ordinary = await bob.SendTextAsync(aliceIdentity.Id, "Normal after disabling timer");
Check(ordinary.DisappearAfterSeconds == 0 && aliceStore.Message(disappearing.Id)!.ExpiresAt is not null, "Turning off self-destruct affects new messages only");
await alice.SetDisappearTimerAsync(group.Id, 10); await Until(() => camStore.Policies().Any(p => p.Id == group.Id && p.DisappearSeconds == 10) && bobStore.Policies().Any(p => p.Id == group.Id && p.DisappearSeconds == 10), "Group expiry policy");
var disappearingGroup = await alice.SendTextAsync(group.Id, "First recipient starts group expiry");
await Until(() => bobStore.Message(disappearingGroup.Id) is not null && camStore.Message(disappearingGroup.Id) is not null, "Expiring group delivery");
await bob.MarkReadAsync(group.Id); await Until(() => camStore.Message(disappearingGroup.Id)?.ExpiresAt is not null && aliceStore.Message(disappearingGroup.Id)?.ExpiresAt is not null, "Group first read sync");
Check(camStore.Message(disappearingGroup.Id)!.Status == "delivered", "First group reader starts expiry even on a member that has not read yet");
await Until(() => aliceStore.Message(disappearing.Id) is null && bobStore.Message(disappearing.Id) is null && aliceStore.Message(disappearingFile.Id) is null && bobStore.Message(disappearingFile.Id) is null && camStore.Message(disappearingGroup.Id) is null, "Automatic expiry", 16000);
await Until(() => !File.Exists(expiringPath), "Expired attachment cleanup");
Check(aliceStore.WasDeleted(disappearing.Id) && !File.Exists(disappearingFile.FilePath) && File.Exists(path), "Expiry removes messages and app-owned media while preserving original source files");
Check(!aliceStore.SaveIncomingMessage(new ChatMessage { Id = disappearing.Id, ConversationId = bobIdentity.Id, Text = "Replay" }), "Expired tombstones prevent old messages returning");
Check(!aliceStore.ExpiredMessages().Any(m => m.Text.Contains("Expires only")), "Expiry receipts retain no deleted text payload");
var unreadRoot = Path.Combine(root, "unread");
using (var unread = new LocalStore(unreadRoot))
{
    for (int i = 0; i < 325; i++) unread.SaveIncomingMessage(new ChatMessage { ConversationId = "first", Status = "delivered", Text = i.ToString() });
    unread.SaveIncomingMessage(new ChatMessage { ConversationId = "second", Kind = "image", Status = "offered" });
    unread.SaveIncomingMessage(new ChatMessage { ConversationId = "second", Kind = "video", Status = "offered" });
    Check(unread.UnreadCount() == 327, "Unread total includes all history, image and video offers");
    Check(unread.UnreadCounts()["first"] == 325 && unread.UnreadCounts()["second"] == 2, "Conversation badges count only their own incoming messages");
    unread.MarkConversationRead("first");
    Check(unread.UnreadCount() == 2, "Mark read covers more than 300 messages and preserves other conversations");
}
using (var unread = new LocalStore(unreadRoot))
{
    Check(unread.UnreadCount() == 2, "Unread count persists across reopening the database");
    var deleted = unread.DeleteConversation("second");
    Check(unread.UnreadCount() == 0 && !unread.SaveIncomingMessage(deleted[0]), "History deletion removes unread and tombstones reject replay");
}
var legacyRoot = Path.Combine(root, "legacy-unread");
using (var legacy = new LocalStore(legacyRoot))
{
    legacy.SaveMessage(new ChatMessage { ConversationId = "legacy", Status = "delivered" });
    legacy.SaveMessage(new ChatMessage { ConversationId = "legacy", Status = "read" });
    legacy.SaveMessage(new ChatMessage { ConversationId = "legacy", Status = "received", Kind = "file" });
    legacy.SaveMessage(new ChatMessage { ConversationId = "legacy", Status = "offered", Kind = "video" });
}
using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(legacyRoot, "history.db")};Pooling=False"))
{
    db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DROP TABLE unread_messages; DROP TABLE local_migrations"; cmd.ExecuteNonQuery();
}
using (var legacy = new LocalStore(legacyRoot)) { Check(legacy.UnreadCount() == 2, "Upgrade restores legacy unread without recounting already seen files"); legacy.MarkConversationRead("legacy"); }
using (var legacy = new LocalStore(legacyRoot)) Check(legacy.UnreadCount() == 0, "Unread migration runs once and does not resurrect read history");
Check(Emoticons.Replace("Chào :) :D ;-) <3 :( :P :O :'( </3") == "Chào 🙂 😃 😉 ❤️ 🙁 😛 😮 😢 💔", "Common emoticons convert to Unicode emoji");
var literal = "https://example.test/:) C:\\:) abc:) `:) <3` ```\n:) <3\n```";
Check(Emoticons.Replace(literal) == literal, "Auto replacement preserves URLs, paths and code");
Check(Emoticons.Replace(":)", false) == ":)" && Emoticons.Replace(":) ", false) == "🙂 " && Emoticons.Replace("Hi :D!") == "Hi 😃!", "Auto replacement waits for a delimiter or sending");
Check(Emoticons.Replace("🙂 ❤️ tiếng Việt") == "🙂 ❤️ tiếng Việt", "Existing emoji and Vietnamese remain unchanged");
Check(!new AppSettings().MessageToasts, "Message toasts are disabled by default");
await UpdaterTests.Run(root, Check);
await PreviewTests.Run(Check);
await AutoImageTests.Run(root, Check);
Console.WriteLine($"\n{passed} checks passed. Test data: {root}");
