// Production browser WebCrypto against the real C# server, loopback-only synthetic data.
import net from "node:net";
import { readFile } from "node:fs/promises";
import assert from "node:assert/strict";
import {
  ByteReader,
  createIdentity,
  secureChannel,
  frame,
  utf8,
  uid,
} from "../crypto.js";
const metadata = JSON.parse(await readFile(process.argv[2], "utf8"));
let passed = 0;
const check = (value, name) => {
  assert.ok(value, name);
  passed++;
  console.log("PASS WEB:", name);
};
async function wire() {
  const socket = net.connect(metadata.port, "127.0.0.1"),
    reader = new ByteReader();
  socket.setNoDelay(true);
  socket.on("data", (b) => reader.push(b));
  socket.on("error", (e) => reader.close(e));
  socket.on("close", () => reader.close());
  await new Promise((resolve, reject) => {
    socket.once("connect", resolve);
    socket.once("error", reject);
  });
  const timer = setTimeout(
    () => socket.destroy(Error("Fixture timeout")),
    10000,
  );
  const t = {
    session: uid(),
    reader,
    closed: false,
    tamper: false,
    last: null,
    write(bytes) {
      if (t.tamper) {
        bytes = bytes.slice();
        bytes[bytes.length - 1] ^= 1;
      }
      t.last = bytes.slice();
      return new Promise((resolve, reject) =>
        socket.write(bytes, (e) => (e ? reject(e) : resolve())),
      );
    },
    close() {
      t.closed = true;
      clearTimeout(timer);
      socket.destroy();
      reader.close();
    },
  };
  return t;
}
async function command(type) {
  const t = await wire();
  try {
    await t.write(frame(utf8(JSON.stringify({ type }))));
    await t.reader.frame();
  } finally {
    t.close();
  }
}
async function connection(identity, pairing = metadata.qr) {
  const t = await wire();
  try {
    await t.write(
      frame(utf8(JSON.stringify({ type: "web-open", id: t.session }))),
    );
    return { t, c: await secureChannel(t, identity, pairing) };
  } catch (e) {
    t.close();
    throw e;
  }
}
const identity = await createIdentity();
await command("fixture-renew");
let pair = await connection(identity);
try {
  await pair.c.write({
    type: "hello",
    name: "Synthetic web QA",
    nonce: metadata.qr.nonce,
  });
  check(
    (await pair.c.read()).type === "welcome",
    "P-256 ECDH/ECDSA and AES-GCM interoperate across JS and .NET",
  );
  const rpc = async (packet) => {
    await pair.c.write({ ...packet, id: packet.id || uid() });
    return pair.c.read();
  };
  const pc = async p => rpc({ type: "pc", pc: p });
  check((await pc({ action: "prepare", command: "power.shutdown" })).type === "error", "Web pairing does not implicitly grant PC commands");
  await command("fixture-pc-grant");
  const rights = (await pc({ action: "status" })).pc;
  check(rights.power && rights.ai && rights.roots[0].path === "", "Encrypted PWA channel exposes permissions and hides physical paths");
  const preparedPc = (await pc({ action: "prepare", command: "power.lock" })).pc;
  const executedPc = (await pc({ action: "execute", ticket: preparedPc.ticket })).pc;
  const retriedPc = (await pc({ action: "execute", ticket: preparedPc.ticket })).pc;
  check(executedPc.state === "done" && retriedPc.message === "fixture call 1", "Web command retries return the original result without another effect");
  const listedPc = (await pc({ action: "files.list", root: rights.roots[0].id })).pc;
  check(listedPc.entries[0].name === "document.txt", "PWA browses a granted PC directory over WebCrypto");
  const filePc = (await pc({ action: "files.open", root: rights.roots[0].id, path: "document.txt" })).pc;
  const dataPc = (await pc({ action: "files.read", ticket: filePc.ticket, offset: 0 })).pc;
  check(Buffer.from(dataPc.data, "base64").toString() === "Lantern PC file fixture", "PWA retrieves selected filesystem bytes independently of chat history");
  await pc({ action: "files.close", ticket: filePc.ticket });
  check((await pc({ action: "ai.inspect" })).pc.targets[0].state === "idle", "PWA receives structured AI status over the authenticated channel");
  let snapshot = await rpc({
    type: "sync",
    conversation: metadata.conversation,
  });
  check(
    snapshot.type === "ok" && snapshot.messages.length >= 30,
    "Load real PC history over encrypted web channel",
  );
  const renamed = await rpc({type:"conversation-rename",conversation:metadata.conversation,name:"Web private alias"});
  check(renamed.conversations.find(c=>c.id===metadata.conversation)?.alias === "Web private alias", "PWA changes its account's private conversation alias");
  const timer = await rpc({type:"conversation-timer",conversation:metadata.conversation,disappearSeconds:300});
  check(timer.conversations.find(c=>c.id===metadata.conversation)?.disappearSeconds === 300, "PWA receives saved self-destruct policy from PC");
  await rpc({type:"conversation-timer",conversation:metadata.conversation,disappearSeconds:0});
  const request = {
    type: "send",
    conversation: metadata.conversation,
    id: uid(),
    text: "Web interop " + uid(),
  };
  const sent = await rpc(request),
    retry = await rpc(request);
  check(
    sent.type === "ok" && retry.messageId === sent.messageId,
    "Retry returns the same committed message",
  );
  snapshot = await rpc({ type: "sync", conversation: metadata.conversation });
  check(
    snapshot.messages.filter((m) => m.id === sent.messageId).length === 1,
    "Retry never creates duplicate bubbles",
  );
  await rpc({
    type: "delete-messages",
    conversation: metadata.conversation,
    messageIds: [sent.messageId],
  });
  check(
    (await rpc(request)).type === "gone",
    "Deleted send cannot be resurrected by retry",
  );
  await command("fixture-lock");
  for (const type of ["conversation-rename","conversation-timer"])
    check((await rpc({type,conversation:metadata.conversation,name:"Denied",disappearSeconds:60})).type === "error", "Locked PWA conversation denies " + type);
  check(
    (
      await rpc({
        type: "delete-messages",
        conversation: metadata.conversation,
        messageIds: [sent.messageId],
      })
    ).type === "error",
    "Locked conversation rejects deletion",
  );
  check(
    (
      await rpc({
        type: "unlock",
        conversation: metadata.conversation,
        password: "wrong",
      })
    ).type === "error",
    "Wrong conversation password is denied",
  );
  check(
    (
      await rpc({
        type: "unlock",
        conversation: metadata.conversation,
        password: "conversation-secret",
      })
    ).type === "ok",
    "Correct conversation password opens web history",
  );
  const second = await rpc({
      type: "send",
      conversation: metadata.conversation,
      text: "Second selected message",
    }),
    third = await rpc({
      type: "send",
      conversation: metadata.conversation,
      text: "Third selected message",
    });
  snapshot = await rpc({
    type: "delete-messages",
    conversation: metadata.conversation,
    messageIds: [second.messageId, third.messageId],
  });
  check(
    !snapshot.messages.some((m) =>
      [second.messageId, third.messageId].includes(m.id),
    ),
    "Batch deletion updates authoritative history",
  );
  snapshot = await rpc({
    type: "clear-history",
    conversation: metadata.conversation,
  });
  check(snapshot.messages.length === 0, "Clear history returns empty snapshot");
  check(
    (await rpc({ type: "sync", conversation: metadata.conversation }))
      .locked === false,
    "Clearing history preserves session unlock and conversation password",
  );
  pair.c.close();
  pair = await connection(identity);
  await pair.c.write({ type: "hello", name: "Synthetic web QA" });
  check(
    (await pair.c.read()).type === "welcome",
    "Approved device reconnects without a fresh QR",
  );
  await pair.c.write({ type: "sync", conversation: metadata.conversation });
  check(
    (await pair.c.read()).locked === true,
    "A new connection must unlock protected history again",
  );
  pair.t.tamper = true;
  await pair.c.write({ type: "list" });
  await assert.rejects(pair.c.read());
  check(true, "Modified AES-GCM ciphertext is rejected");
} finally {
  pair.c.close();
}
await assert.rejects(
  connection(identity, { ...metadata.qr, pc: "A".repeat(64) }),
);
check(true, "A different pinned PC identity is rejected");
await assert.rejects(
  connection(identity, {
    ...metadata.qr,
    relay: { ...metadata.qr.relay, id: "a".repeat(32) },
  }),
);
check(true, "Handshake binds the route capability");
const other = await createIdentity();
await assert.rejects(connection({ ...identity, key: other.key }));
check(true, "Identity key and signature must match");
pair = await connection(identity);
try {
  await pair.c.write({ type: "hello", name: "Synthetic web QA" });
  await pair.c.read();
  const old = pair.t.last;
  await pair.t.write(old);
  await assert.rejects(pair.c.read());
  check(true, "Encrypted record replay fails sequence authentication");
} finally {
  pair.c.close();
}
console.log(passed + " web interoperability checks passed");
