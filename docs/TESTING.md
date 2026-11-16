# Verification

Automated integration runner uses real TCP sockets and mutual TLS on IPv4 loopback, independent identities, SQLite stores and temporary data. It exits nonzero on failure.

The current core suite passes 94 checks covering:

- Persistent identity, pairing codes, trust gating and certificate pinning.
- UTF-8 chat, durable delivery/read receipts and replay deduplication.
- Endpoint normalization, offline queue and reconnect to a changed endpoint.
- Three-peer group delivery.
- Explicit file consent, resume from a partial file, empty files, SHA-256 failure and retry, decline.
- Filename traversal prevention, network/frame limits.
- Identity backup/import and invalid password rejection.
- History/media reset, tombstones and new messages after reset.
- Windows owner binding and invalid/tampered reset prerequisites.
- AES-GCM, tamper rejection, no plaintext message payload in SQLite.
- Mandatory workspace key to read history, separate conversation password and persistence.
- Wrong master password, password rotation, old password invalidation and history preservation.
- Null-field protocol validation, password-protected private network key and bidirectional networking from an encrypted workspace.
- Profile propagation, private local aliases, clearing aliases and persistence.
- Durable unread totals and conversation counts, attachment read state, deduplication and migration from older history.
- Emoticon conversion with URL/path/code preservation; notification opt-in defaults.
- Reactions, reaction removal, stale revision rejection, group reactions and quoted replies.
- Contact sharing and name lookup through a trusted peer without implicit trust or alias disclosure.
- Idle and do-not-disturb presence updates.
- Disappearing-message policies, read-triggered synchronized deadlines, group first-read behavior, file cleanup and tombstones.
- Update release selection, SHA-256 and size verification, cache cleanup on failure, repository restrictions and ZIP traversal rejection.

`tests/Lantern.UiTests` is a separate fixture host that runs the real WPF MainWindow with synthetic encrypted data and a real loopback peer. It does not modify production authentication or touch the user's workspace. It passes 16 UI checks: tray and conversation badges while minimized, session conversation unlock, alias header, emoticon typing, quote delivery, reaction delivery, clipboard-image transfer, split media/link panels, presence colors, emoji picker, manual lock, unread while locked and toast opt-in. It captures light/dark and emoji-picker renders for visual review. Run it only on an interactive Windows desktop.

UI verification before password-gate integration exercised a real peer connection, image receipt/display and MP4 receipt/playback on the Windows desktop. The release is also opened at its startup password screen. OS credential prompts are deliberately not automated: the owner completes Windows Hello on their own device.

Not verified: two physical LAN hosts, enterprise firewall/VLAN policies, sustained 32-member groups, 10 GB transfers, every Windows codec, all high-contrast/DPI combinations, or successful destructive Windows Hello approval with the user's credentials.
