# Lantern encrypted relay

Worker module + D1 migrations for the private Sites relay. Source contains no deployment credentials. The live deployment is managed through the separate Sites checkout; copy changes from this directory and apply the migration manifest there before publishing.

The Sites owner-private access boundary requires `OAI-Sites-Authorization`; routes additionally require independent host/client bearer keys. Clients carry end-to-end mutual TLS over the packet endpoint. The Worker never terminates that inner TLS session.

Each exchange authenticates and updates the queue in one atomic D1 batch. Every packet read and mutation checks the route key, active session and expiry. Clients can request `batch: 4` to drain up to four ordered packets or `pushOnly: true` to send independently of the receive loop; old clients retain the one-packet response. Queues remain bounded to 32 packets per direction. Acknowledgements commit even when a new packet hits backpressure, and conflicting retries are rejected.

Run `npm test` with Node 22.13 or newer. The tests use real SQLite behind a minimal D1 adapter. Production hosting also needs a Sites project configured with D1 binding `DB` and the migration in `drizzle/0000_relay.sql`. Do not put service tokens into this repository or public app builds.
