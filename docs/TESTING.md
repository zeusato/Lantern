# Verification

Automated integration runner uses real TCP sockets and mutual TLS on IPv4 loopback, independent identities, SQLite stores and temporary data. It exits nonzero on failure.

The core suite covers:

- Persistent identity, pairing codes, trust gating and certificate pinning.
- UTF-8 chat, durable delivery/read receipts and replay deduplication.
- Endpoint normalization, offline queue and reconnect to a changed endpoint.
- Three-peer group delivery.
- Explicit document/video consent, resume from a partial file, empty files, SHA-256 failure and retry, decline.
- Automatic image queue, five-image burst, three concurrent transfers, resumed images and unread/expiry state until download completes.
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
- Emoticon conversion with URL/path/code preservation; enabled/private notification defaults and preserved explicit opt-out.
- Reactions, reaction removal, stale revision rejection, group reactions and quoted replies.
- Contact sharing and name lookup through a trusted peer without implicit trust or alias disclosure.
- Idle and do-not-disturb presence updates.
- Disappearing-message policies, read-triggered synchronized deadlines, group first-read behavior, file cleanup and tombstones.
- Update release selection, SHA-256 and size verification, cache cleanup on failure, repository restrictions and ZIP traversal rejection.
- Open Graph metadata parsing, image loading/cache and private-address/redirect rejection.
- Full/private notification content, locked-conversation privacy and persisted toast preference.
- Bounded subnet discovery, non-/24 masks, multi-interface deduplication, real TCP fallback with UDP disabled, unicast discovery of dynamic ports, malformed datagrams and cancellation.
- Owner-only member additions, old-client capability gates, history enabled/disabled, original-author history replay, downloadable historical attachments, durable per-member permissions and stale/conflicting roster rejection.
- Message URL extraction with punctuation, balanced parentheses, queries, Unicode surroundings and non-browser scheme exclusion.
- Signed group leave/owner handoff/deletion, designated and random succession, last-member cleanup, signature tampering, former-owner denial and offline deletion relayed by another member.
- Group owner-only rules, direct permission synchronization, rejected forwarding and independent forwarded text/contact/file copies; unsupported native mobile clients cannot fetch capture-protected history. Authenticated PWA sessions remain usable with a capture advisory and still enforce conversation passwords.

The conversation UI fixture additionally checks both context menus, local pins/categories, online-before-offline ordering, composer autofocus, the native Windows capture-affinity flag and restoration, owned-window protection, reactor lists/explicit removal, five-name tooltips and the expanded hover delay. Avatar checks exercise square export, movement/clamping, zoom, persistence, peer sync and removal; `avatar-crop.png` records the editor. The web model tests cover removal of deleted cached content, encrypted boxes, drafts and pending sends, while capture-rule changes preserve readable web history and drafts. Android Java compilation checks the corresponding cache invalidation and secure dialog changes. These checks do not certify third-party screen recorders or modified clients.

Additional UI checks verify clickable message links and lossless copy with emoji; a real HTML transfer started by one **Mở HTML** click; WebView2 CSS/JavaScript execution and button interaction; default network isolation, no app bridge, local-file blocking; and closure through the existing media-view lifecycle. Tests use synthetic files and a separate browser profile, never a user's browser or chat history.

`tests/Lantern.UiTests` is a separate fixture host that runs the real WPF MainWindow with synthetic encrypted data and a real loopback peer. It does not modify production authentication or open the user's history. Its standard 70 checks cover tray/taskbar/conversation badges, content-sized left/right message bubbles, lower-edge reaction controls, actual reaction delivery/removal, quotes, colored emoji, clipboard repair, PNG transfer/rendering, automatic image receipt, square thumbnails, Open Graph cards, presence, full information-pane locks, toast/tray settings, embedded login and conversation prompts, real correct/incorrect password verification, password autofocus, account-name greetings, new-workspace creation and unchanged native window handles across startup authentication. Two optional legacy-image/video fixture checks remain available but were not rerun for this release. Screenshots cover light/dark chat, message layout, reactions, locked information, in-app password prompts and login. The QA host explicitly restores focus to its own native window after fixture dialogs; it never activates or opens a user workspace. Run it only on an interactive Windows desktop.

UI verification before password-gate integration exercised a real peer connection, image receipt/display and MP4 receipt/playback on the Windows desktop. Cold startup and new-workspace creation are exercised in the same WPF window using synthetic credentials. OS credential prompts are deliberately not automated: the owner completes Windows Hello on their own device.

Not verified: two physical LAN hosts, enterprise firewall/VLAN policies, sustained 32-member groups, 10 GB transfers, every Windows codec, all high-contrast/DPI combinations, or successful destructive Windows Hello approval with the user's credentials.

`scripts/Test-UpdateInstaller.ps1` exercises the embedded update helper with Windows PowerShell 5.1 and a synthetic Windows executable: waiting for exit, backup, replacement, restart, and preserving a workspace argument containing spaces. No real app workspace is used. The script's UTF-8 BOM and the runtime's explicit BOM output preserve Vietnamese text under Windows PowerShell.

Tray lifecycle checks cover the opt-in setting and its disk persistence, close-to-tray without disconnect, receiving unread messages while hidden, restoring the same session, closing the unlock prompt without exposing chat, reopening and entering the real synthetic workspace password, the actual tray Exit menu action, and normal close behavior when the setting is disabled.

## Rich text, polls, clipboard and companion login

Run the core suite with `--rich-polls-only` for rich payload validation and real three-peer poll delivery, vote changes, offline retries, ownership and expiry; `--mobile-login-only` checks the PC-account gate, wrong-password throttling, logout, password rotation and persisted downgrade prevention over TLS. Both also run in the full core suite.

The WPF `--rich-polls <artifact-directory>` fixture checks formatting round trips, lists, links spanning styles, emoji undo, conversation drafts, expanded/compact layouts, sent-message persistence and protected-log hiding. Image menu checks verify full-resolution Bitmap/PNG clipboard payloads with alpha and disabled copying before download. It renders screenshots without opening the user workspace.

`node pwa/tests/chat-ui.mjs` uses Playwright with synthetic state and the real app module to check capture notices, back/reopen, drafts, send, info and password-lock redraw. `LANTERN_PLAYWRIGHT` can point to a local Playwright module; `LANTERN_UI_SCREENSHOT` records the 390px view. The WebInteropFixture plus `pwa/tests/interop.mjs` verifies actual encrypted PWA/PC history and sending while capture protection is enabled.

Android AppLoginTest covers the login gate and stale authentication responses; live flow fixtures authenticate with a synthetic account password. APK and instrumentation APK compile, and lint passes. Android instrumentation still needs a connected device/emulator; compilation alone does not verify the device UI.
