// Optional live relay QA. Credentials enter through hidden stdin, never files or logs.
import net from "node:net";
import { readFile } from "node:fs/promises";
import assert from "node:assert/strict";
import { RelayTransport } from "../channel.js";
import {
  ByteReader,
  createIdentity,
  secureChannel,
  frame,
  utf8,
  uid,
  b64,
  random,
} from "../crypto.js";
const metadata = JSON.parse(await readFile(process.argv[2], "utf8"));
process.stdin.setRawMode?.(true);
process.stdin.setEncoding("utf8");
process.stdin.resume();
console.log("Ready for private relay configuration (hidden).");
let input = "";
process.stdin.on("data", async (chunk) => {
  input += chunk;
  if (!/[\r\n]/.test(input)) return;
  process.stdin.pause();
  process.stdin.setRawMode?.(false);
  let config;
  try {
    config = JSON.parse(input);
    input = "";
    await run(config);
    process.exit(0);
  } catch (e) {
    console.error("Relay QA failed:", e.message);
    process.exit(1);
  }
});
async function run(config) {
  const origin = new URL(config.url);
  assert.equal(origin.protocol, "https:");
  const prefix = "/v1/routes/" + metadata.qr.relay.id;
  const fetcher = (url, options) =>
    fetch(new URL(url, origin), {
      ...options,
      headers: {
        ...options.headers,
        "OAI-Sites-Authorization": "Bearer " + config.serviceToken,
      },
    });
  const hostKey = b64(random(32))
    .replace(/=/g, "")
    .replace(/\+/g, "-")
    .replace(/\//g, "_");
  const call = async (action, key, body) => {
    const r = await fetcher(action, {
      method: "POST",
      headers: {
        Authorization: "Bearer " + key,
        "Content-Type": "application/json",
      },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(20000),
    });
    assert.equal(r.status, 200, "Relay status " + r.status);
    return r.json();
  };
  const command = async (value) => {
    const r = new ByteReader(),
      s = net.connect(metadata.port, "127.0.0.1");
    s.on("data", (b) => r.push(b));
    s.on("error", (e) => r.close(e));
    await new Promise((y) => s.once("connect", y));
    s.write(frame(utf8(JSON.stringify(value))));
    await r.frame();
    s.destroy();
  };
  await command({ type: "fixture-renew" });
  const issuedAt = Math.floor(Date.now() / 1000),
    signingKey = await crypto.subtle.importKey(
      "raw",
      utf8(config.serviceToken),
      { name: "HMAC", hash: "SHA-256" },
      false,
      ["sign"],
    );
  const signature = Buffer.from(
    await crypto.subtle.sign(
      "HMAC",
      signingKey,
      utf8(
        [
          "Lantern-route-v1",
          metadata.qr.relay.id,
          hostKey,
          metadata.qr.relay.key,
          String(issuedAt),
        ].join("\n"),
      ),
    ),
  ).toString("hex");
  await call("/v1/routes", hostKey, {
    id: metadata.qr.relay.id,
    clientKey: metadata.qr.relay.key,
    registration: { issuedAt, signature },
  });
  // Public web visitors use only their QR route capability, never the Sites credential.
  const browserFetch = (url, options) => fetch(new URL(url, origin), options);
  const client = new RelayTransport(
      metadata.qr,
      config.public ? browserFetch : fetcher,
    ),
    host = new RelayTransport(
      { relay: { id: metadata.qr.relay.id, key: hostKey } },
      fetcher,
    );
  host.session = client.session;
  const socket = net.connect(metadata.port, "127.0.0.1");
  socket.setNoDelay(true);
  socket.on("error", (e) => host.close(e));
  await new Promise((y) => socket.once("connect", y));
  socket.write(
    frame(utf8(JSON.stringify({ type: "web-open", id: client.session }))),
  );
  socket.on("data", (bytes) => host.write(bytes).catch(() => socket.destroy()));
  const deadline = setTimeout(() => {
    client.close();
    host.close();
    socket.destroy();
  }, 90000);
  try {
    const identity = await createIdentity(),
      start = performance.now();
    await client.open();
    host.receive();
    const incoming = (async () => {
      try {
        while (!host.closed)
          socket.write(frame(await host.reader.frame(65536)));
      } catch {
        socket.destroy();
      }
    })();
    const channel = await secureChannel(client, identity, metadata.qr);
    await channel.write({
      type: "hello",
      name: "Synthetic relay QA",
      nonce: metadata.qr.nonce,
      capabilities: ["push-v1"],
    });
    assert.equal((await channel.read()).type, "welcome");
    console.log(
      JSON.stringify({
        stage: "pairing",
        milliseconds: Math.round(performance.now() - start),
      }),
    );
    const pending = new Map();
    let onPush = () => {};
    const reading = (async () => {
      try {
        while (!client.closed) {
          const p = await channel.read();
          if (pending.has(p.id)) {
            pending.get(p.id)(p);
            pending.delete(p.id);
          } else onPush(p);
        }
      } catch {}
    })();
    const rpc = async (value) => {
      const id = value.id || uid(),
        promise = new Promise((resolve) => pending.set(id, resolve));
      await channel.write({ ...value, id });
      return promise;
    };
    const t = performance.now(),
      history = await rpc({
        type: "sync",
        conversation: metadata.conversation,
      });
    assert.equal(history.type, "ok");
    console.log(
      JSON.stringify({
        stage: "history",
        count: history.messages.length,
        milliseconds: Math.round(performance.now() - t),
      }),
    );
    const timings = [];
    for (let i = 0; i < 5; i++) {
      const start = performance.now(),
        packet = {
          type: "send",
          conversation: metadata.conversation,
          text: "Relay QA " + i,
          id: uid(),
        },
        sent = await rpc(packet);
      assert.equal(sent.type, "ok");
      const ack = Math.round(performance.now() - start),
        retry = await rpc(packet);
      assert.equal(retry.messageId, sent.messageId);
      const incomingText = "Incoming relay QA " + uid(),
        waiting = new Promise(
          (resolve) =>
            (onPush = (p) => {
              if (p.messages?.some((m) => m.text === incomingText)) resolve();
            }),
        );
      const receiveStart = performance.now();
      await command({ type: "fixture-message", text: incomingText });
      await waiting;
      const push = Math.round(performance.now() - receiveStart);
      timings.push({ ack, push });
      console.log(
        JSON.stringify({ sample: i + 1, sendAckMs: ack, incomingPushMs: push }),
      );
    }
    assert.ok(
      timings.every((t) => t.ack < 2000 && t.push < 2000),
      "Live relay exceeded 2s on at least one sample",
    );
    console.log(
      "PASS live encrypted relay, durable send, duplicate retry and incoming push",
    );
  } finally {
    clearTimeout(deadline);
    client.close();
    host.close();
    socket.destroy();
    await call(prefix + "/revoke", hostKey, {});
  }
}
