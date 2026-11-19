# Lantern protocol · 1.1 extensions

## Transport and identity

TCP with .NET SslStream, TLS 1.2/1.3, mutual ECDSA P-256 certificates. Identity is uppercase hex SHA-256 of certificate SPKI. The remote certificate callback permits a self-signed certificate only if it matches the expected identity when one is known, rejects the local identity and blocked peers, and leaves authorization to the persisted trusted-peer table. The first encrypted `hello` must repeat the identity proven by TLS.

The first pairing compares all 96 bits of `SHA256("Lantern-pair-v1:" + orderedPeerIdsSeparatedByColon)[0..12]` via an independent trusted channel. The UI renders six groups of four hex characters. Accepting a name/IP without comparing this code is not secure pairing.

No credentials are transmitted. No Windows trusted-root certificate is installed. Private keys in application workspaces are encrypted under the workspace key and then DPAPI-protected at rest. Windows Schannel uses a temporary OS-backed key container while the certificate is alive because Windows does not support ephemeral key handles for this use case.

## Framing

Each TLS message consists of a 4-byte signed big-endian length and UTF-8 JSON. Length must be 2–262144 bytes before allocating the payload. JSON uses `WireMessage`. Application IDs are 32 hex-character UUIDs. Text is at most 16000 characters.

Operations:

| Type | Meaning |
|---|---|
| `hello` | Peer ID, display name, listening port; required first |
| `ready` | Local peer has authorized the remote identity; initiates replay |
| `ping` | Keepalive |
| `message` | Direct/group text, authenticated sender ID |
| `ack`, `read` | Persisted receipt / read receipt |
| `group` | Owner-authorized group roster with monotonic revision and per-member history access |
| `group-action` | Signed member departure, owner handoff or terminal group deletion, relayable by trusted peers |
| `conversation-rules` | Direct-chat capture/forward permissions, ordered by revision then authenticated actor |
| `offer` | File metadata and SHA-256, no file contents |
| `accept` | Recipient acceptance (automatic for images under local policy) and resume byte offset |
| `chunk` | 64 KB or smaller chunk, Base64-encoded with exact byte offset |
| `end` | Finish and verify local size/SHA-256 |
| `fileDone` | Recipient has validated and stored the complete file |
| `decline` | Recipient declines/stops the transfer |
| `profile` | Authenticated public display name and bounded Base64 PNG avatar in `Data`; never carries a local alias |
| `presence` | `online`, `idle` or `dnd`; disconnected sessions render offline |
| `reaction` | Message ID, the authenticated actor's emoji (empty means removal), monotonically increasing revision |
| `contact` | Chat message carrying a public contact card; no trust transfer |
| `lookup`, `lookup-result` | Bounded name search through trusted connected peers; up to 20 public cards per response |
| `expiry-policy` | Conversation timer for future messages, ordered by revision then authenticated actor |
| `expiry-start` | A read-triggered deadline for a specific expiring message |

Current hello includes `Version: 5` and presence. Version 2 enables self-destruct messages; version 3 enables adding group members; versions 4 and 5 add the lifecycle and permission controls described below. Peers with missing/older timer capability cannot receive newly sent self-destruct messages; pending self-destruct sends remain queued until a compatible peer connects. Normal v1 chat remains compatible. Quoted text uses `ReplyToId` and resolves the source only inside the same local conversation. Unknown/expired sources show an unavailable reference instead of copying deleted content.

Unread markers are stored separately from transfer status. Reading visible unlocked conversations clears markers and persists read receipts. Image offers still waiting/downloading retain unread markers and do not start their expiry timer until downloaded and read. Message replay and reactions do not increment unread totals. A conversation password is unlocked for the process session until explicitly locked or the whole application is locked/closed.

Self-destruct TTL travels with each message. The first recipient to read starts a UTC deadline; group members converge on the earliest authenticated deadline. Offline peers receive deadlines again on reconnect. Expiration removes message payload, unread markers and owned media, and retains only minimal encrypted deadline tombstones for synchronization. Reactions replay only the local actor's latest revision, including removals. Group membership and direct-peer identity are checked before applying either operation.

Before local trust, only hello and keepalive can have useful effects; chat and file operations are ignored. Once trusted, sender identity is checked against TLS. Group messages are accepted only for locally stored membership including both sender and recipient. Only the authenticated group owner can append members. An update cannot replace the owner, remove existing members, or change their history permissions; stale revisions are ignored and conflicting equal revisions are rejected.

Existing groups start at revision 0. Each added member records `JoinedRevision` and `IncludeHistory`; each new group message records `GroupRevision`. With history disabled, messages older than the recipient's join revision are rejected both when queued/replayed and when received. A member cannot author messages predating their own join revision. Adding members requires every member's last authenticated hello to support version 3. The owner replays the latest roster on reconnect; each original author adds eligible recipients to their own persisted outbox, so third parties never impersonate the author. History retains original IDs, timestamps and file hashes, respects deletion tombstones and expiry, and does not create notification bursts. Existing pairwise trust is still required for history and attachment transfer.

Hello version 4 supports group lifecycle; version 5 adds shared capture/forward rules. Lifecycle actions require all current members to advertise at least 4; changing shared rules requires 5. Adding members to a transferred/protected group uses the corresponding higher minimum. An ECDSA-SHA256 signature covers `Lantern-group-action-v1\n` plus the exact JSON payload, whose SPKI hash must match the actor. Group snapshots carry `OwnerGeneration` and `OwnershipId`; only signed handoffs advance ownership, and only the current owner can delete. Signed departures remove the matching membership incarnation. Terminal deletion retains signed control records and a group tombstone, purges local messages/unread/media, and cannot be undone by stale rosters or delayed messages. Peers relay control records on reconnect, including to former members. This relay does not relay chat content. Leaving keeps already received history but disables group sending.

Direct-chat rules are TLS-authenticated; group rules travel in the owner's versioned roster. Forwarding checks the source log permission, creates a new message authored by the forwarding user, and marks `Forwarded`; it never impersonates the original author. These are cooperative client permissions, not DRM. Windows applies capture exclusion to the active chat window and owned media windows, restoring ordinary capture on another log. Native mobile clients without `capture-protection-v1` receive metadata only for protected logs. Authenticated web-v1 sessions are exempt from capture blocking: the PWA shows a nonblocking notice because browsers cannot enforce OS screenshot exclusion. This exemption is determined by the authenticated transport, not a client capability claim. Conversation password and device access checks still apply. Full mobile conversation snapshots set `ConversationSnapshot`, allowing an empty list to invalidate deleted conversations and their cached media/outbox. Capture changes do not erase PWA history or drafts.

Avatars are optional PNGs of exactly 192 × 192, at most 160 KiB decoded, sent only in authenticated profile updates and replay. Empty data removes the photo. The desktop editor strips original image metadata by rendering the chosen square into a fresh PNG. Own profile data and received avatars use encrypted store payloads when the workspace is encrypted; discovery and contact lookup do not carry avatars.

Discovery announcements retain `lantern-v1`. A `lantern-probe` UDP packet with the same validated identity/name/TCP-port fields receives one unicast announcement at its source endpoint, rate limited per source address. Announcements are never echoed. The manual search probes bounded local subnets, uses unicast replies to learn dynamic ports, and falls back to TCP/TLS on the default peer port for old clients. Discovery metadata never grants trust. Multicast joins refresh when interfaces change; multicast and each broadcast destination fail independently.

## Durability and replay

SQLite WAL with synchronous FULL persists outgoing messages before transmission and incoming messages before ACK. IDs are globally unique and duplicate deliveries acknowledge existing data instead of inserting another message. Per-recipient status updates happen under the same store lock to avoid lost group acknowledgments.

Reconnect sends owner group definitions before pending messages. Concurrent replay requests are coalesced into a follow-up pass so an update arriving during replay is not deferred to the periodic retry. Pending messages are retried periodically while connected; this also recovers group invitation ordering races. Every peer keeps its own copies. There is no globally ordered transcript, distributed consensus or offline relay.

Attachments are first copied into the sender's media cache, then hashed, so a subsequent modification of the original file cannot alter the advertised content. The recipient sanitizes names, generates a storage path from the UUID, limits sizes, requires receiver acceptance and verifies offsets. The Windows app automatically accepts image offers only from trusted, unblocked peers, queues at most three transfers and resumes pending images on reconnect. Documents and videos still require a manual acceptance. A `.part` file is renamed only after SHA-256 and length match. Received executable attachments are never launched automatically.

## Operational limits

64 live peer sessions, 12 concurrent TLS handshakes, 256 discovered peer entries, 32 group members, 3 concurrent accepted incoming files and 10 GB per file. These are defensive implementation limits, not a statement that maximum-scale performance was benchmarked.

Discovery UDP metadata is untrusted and can be spoofed or suppressed; certificate identity and human verification provide trust. Discovery poisoning may still deny availability. Same-LAN peers can observe discovery names/IDs/IPs, even though chat and attachments use encrypted connections.

No application-layer ratchet, multi-device identity protocol, group key distribution or central revocation server is implemented. Self-destruct relies on each client honoring the protocol; it cannot erase copies made outside Lantern or control modified clients. A local administrator or compromised endpoint can access local data. TLS protects traffic, not a compromised endpoint.

## Workspace credentials and local locks

Application startup requires a password before opening the history store or starting the network listener. A random 256-bit workspace key is wrapped with AES-256-GCM under PBKDF2-HMAC-SHA256(password, 24-byte salt, 600000 iterations). The wrapped key is bound by associated data to workspace ID and Windows owner SID. A separate DPAPI owner proof binds that same ID/SID for destructive authorization even when the app password has been forgotten.

SQLite JSON payloads for messages, peers and groups are AES-GCM-encrypted with fresh 96-bit nonces under the workspace key. IDs and times remain metadata. Media files remain ordinary local files. Conversation password records use independent salts and PBKDF2-HMAC-SHA256 hashes, stored under workspace encryption. Conversation passwords enforce local viewing gates; they are not independent conversation encryption keys.

Destructive reset requires the exact per-workspace phrase, matching Windows SID, intact DPAPI owner proof, and a fresh successful Windows UserConsentVerifierInterop request bound to the owner window. Windows Hello unavailable/canceled/failed is fail-closed. The approved workspace ID is rechecked immediately before deleting only app-owned paths. No command-line bypass or test bypass for password/Hello gates is shipped.

Local conversation deletion atomically writes message-ID tombstones and deletes message records, then deletes its media. Incoming retries for tombstoned IDs cannot recreate deleted content. Other conversations and peer copies are not affected.

## Android account authentication

Android advertises `app-password-v1` in the TLS hello. The PC persists that requirement in the encrypted device registry; reconnecting without the capability cannot downgrade an opted-in device. Older companions that have never opted in retain their existing protocol behavior.

Each new required session starts locked. `app-login` verifies the PC workspace password against the current vault header and returns `app-authenticated` only on success. Attempts are throttled per paired device. `app-status` exposes only login state; `app-logout` clears account and conversation unlock state. Before login, list/sync/push return `app-locked` without contacts or messages and without `ConversationSnapshot`; all other chat, media and PC actions are denied. A password change is effective for the next login. No reusable password token is issued.

Android keeps login state only in memory. Backgrounding, service shutdown and transport loss invalidate the session, clear visible history and require login again. Generation checks reject a login response arriving after a lock. Configuration changes and navigation between activities in the app retain the session. The PC must be reachable to authenticate, even when encrypted history is cached.

Rich message and group poll formats, compatibility and replay rules are documented in [CHAT-COMPOSER-POLLS.md](CHAT-COMPOSER-POLLS.md).
