# Lantern encrypted relay

Worker module + D1 migrations for the private Sites relay. Source contains no deployment credentials. The live deployment is managed through the separate Sites checkout; copy changes from this directory and apply the migration manifest there before publishing.

The mobile PWA is public. Route traffic requires independent random host/client bearer keys. New route registration additionally requires a short-lived HMAC proof from an authorized PC, verified by the secret runtime variable `LANTERN_REGISTRATION_KEY`; provision it to match that deployment's native PC relay credential. Never put the value in source or a client bundle. Existing routes may renew with their host key for backward compatibility. Native clients carry end-to-end mutual TLS; the PWA uses authenticated P-256 ECDH and AES-GCM records. The Worker never terminates either inner encrypted channel.

Each exchange authenticates and updates the queue in one atomic D1 batch. Every packet read and mutation checks the route key, active session and expiry. Clients can request `batch: 4` to drain up to four ordered packets or `pushOnly: true` to send independently of the receive loop; old clients retain the one-packet response. Queues remain bounded to 32 packets per direction. Acknowledgements commit even when a new packet hits backpressure, and conflicting retries are rejected.

Run `npm test` with Node 22.13 or newer. The tests use real SQLite behind a minimal D1 adapter. Production hosting also needs a Sites project configured with D1 binding `DB` and the migration in `drizzle/0000_relay.sql`. Do not put service tokens into this repository or public app builds.

Build public web assets into the Worker with `npm ci --prefix pwa` then `node relay/scripts/build-web.mjs` from the repository root. The build fingerprints static assets to invalidate the service worker cache on every source change. Deploy the guarded registration endpoint and configure its runtime secret before switching an existing private Site to public access.
