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
| `group` | Immutable group definition, only from authenticated owner |
| `offer` | File metadata and SHA-256, no file contents |
| `accept` | Recipient consent and resume byte offset |
| `chunk` | 64 KB or smaller chunk, Base64-encoded with exact byte offset |
| `end` | Finish and verify local size/SHA-256 |
| `fileDone` | Recipient has validated and stored the complete file |
| `decline` | Recipient declines/stops the transfer |
| `profile` | Authenticated public display name update; never carries a local alias |
| `presence` | `online`, `idle` or `dnd`; disconnected sessions render offline |
| `reaction` | Message ID, the authenticated actor's emoji (empty means removal), monotonically increasing revision |
| `contact` | Chat message carrying a public contact card; no trust transfer |
| `lookup`, `lookup-result` | Bounded name search through trusted connected peers; up to 20 public cards per response |
| `expiry-policy` | Conversation timer for future messages, ordered by revision then authenticated actor |
| `expiry-start` | A read-triggered deadline for a specific expiring message |

Current hello includes `Version: 2` and presence. Peers with missing/older capability versions cannot receive newly sent self-destruct messages; pending self-destruct sends remain queued until a compatible peer connects. Normal v1 chat remains compatible. Quoted text uses `ReplyToId` and resolves the source only inside the same local conversation. Unknown/expired sources show an unavailable reference instead of copying deleted content.

Unread markers are stored separately from transfer status. Reading visible unlocked conversations clears markers and persists read receipts. Message replay and reactions do not increment unread totals. A conversation password is unlocked for the process session until explicitly locked or the whole application is locked/closed.

Self-destruct TTL travels with each message. The first recipient to read starts a UTC deadline; group members converge on the earliest authenticated deadline. Offline peers receive deadlines again on reconnect. Expiration removes message payload, unread markers and owned media, and retains only minimal encrypted deadline tombstones for synchronization. Reactions replay only the local actor's latest revision, including removals. Group membership and direct-peer identity are checked before applying either operation.

Before local trust, only hello and keepalive can have useful effects; chat and file operations are ignored. Once trusted, sender identity is checked against TLS. Group messages are accepted only for locally stored membership including both sender and recipient. A group invitation cannot overwrite ownership or membership of an existing group.

## Durability and replay

SQLite WAL with synchronous FULL persists outgoing messages before transmission and incoming messages before ACK. IDs are globally unique and duplicate deliveries acknowledge existing data instead of inserting another message. Per-recipient status updates happen under the same store lock to avoid lost group acknowledgments.

Reconnect sends owner group definitions before pending messages. Pending messages are retried periodically while connected; this also recovers group invitation ordering races. Every peer keeps its own copies. There is no globally ordered transcript, distributed consensus or offline relay.

Attachments are first copied into the sender's media cache, then hashed, so a subsequent modification of the original file cannot alter the advertised content. The recipient sanitizes names, generates a storage path from the UUID, limits sizes, requires consent and verifies offsets. A `.part` file is renamed only after SHA-256 and length match. Received executable attachments are never launched automatically.

## Operational limits

64 live peer sessions, 12 concurrent TLS handshakes, 256 discovered peer entries, 32 group members, 3 concurrent accepted incoming files and 10 GB per file. These are defensive implementation limits, not a statement that maximum-scale performance was benchmarked.

Discovery UDP metadata is untrusted and can be spoofed or suppressed; certificate identity and human verification provide trust. Discovery poisoning may still deny availability. Same-LAN peers can observe discovery names/IDs/IPs, even though chat and attachments use encrypted connections.

No application-layer ratchet, multi-device identity protocol, group key distribution or central revocation server is implemented. Self-destruct relies on each client honoring the protocol; it cannot erase copies made outside Lantern or control modified clients. A local administrator or compromised endpoint can access local data. TLS protects traffic, not a compromised endpoint.

## Workspace credentials and local locks

Application startup requires a password before opening the history store or starting the network listener. A random 256-bit workspace key is wrapped with AES-256-GCM under PBKDF2-HMAC-SHA256(password, 24-byte salt, 600000 iterations). The wrapped key is bound by associated data to workspace ID and Windows owner SID. A separate DPAPI owner proof binds that same ID/SID for destructive authorization even when the app password has been forgotten.

SQLite JSON payloads for messages, peers and groups are AES-GCM-encrypted with fresh 96-bit nonces under the workspace key. IDs and times remain metadata. Media files remain ordinary local files. Conversation password records use independent salts and PBKDF2-HMAC-SHA256 hashes, stored under workspace encryption. Conversation passwords enforce local viewing gates; they are not independent conversation encryption keys.

Destructive reset requires the exact per-workspace phrase, matching Windows SID, intact DPAPI owner proof, and a fresh successful Windows UserConsentVerifierInterop request bound to the owner window. Windows Hello unavailable/canceled/failed is fail-closed. The approved workspace ID is rechecked immediately before deleting only app-owned paths. No command-line bypass or test bypass for password/Hello gates is shipped.

Local conversation deletion atomically writes message-ID tombstones and deletes message records, then deletes its media. Incoming retries for tombstoned IDs cannot recreate deleted content. Other conversations and peer copies are not affected.
