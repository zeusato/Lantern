import { test } from "node:test";
import assert from "node:assert/strict";
import { joins, reconcile, applySnapshot, emoticons } from "../model.js";
import { validatePairing } from "../channel.js";
import { invalidateDeletedHistory } from "../model.js";
import { passwordKey, seal, unseal } from "../vault.js";
import {
  random,
  ByteReader,
  frame,
  utf8,
  text,
  verificationCode,
} from "../crypto.js";
test("grouping boundaries, directions and midnight", () => {
  const a = {
    senderId: "a",
    conversationId: "c",
    outgoing: false,
    time: new Date(2026, 10, 17, 10).toISOString(),
  };
  const b = { ...a, time: new Date(Date.parse(a.time) + 300000).toISOString() };
  assert.equal(joins(a, b), true);
  assert.equal(
    joins(a, { ...b, time: new Date(Date.parse(b.time) + 1).toISOString() }),
    false,
  );
  assert.equal(joins(a, { ...b, senderId: "b" }), false);
  assert.equal(joins(a, { ...b, outgoing: true }), false);
  assert.equal(joins(a, { ...b, conversationId: "d" }), false);
  assert.equal(joins(b, a), false);
  assert.equal(
    joins(
      { ...a, time: new Date(2026, 10, 17, 23, 59).toISOString() },
      { ...a, time: new Date(2026, 10, 18, 0, 0).toISOString() },
    ),
    false,
  );
});
test("optimistic messages reconcile without duplicates and expired content stays hidden", () => {
  const old = { id: "a", conversationId: "c", time: "2026-11-17T00:00:00Z" };
  const pending = { ...old, _pending: true };
  const second = { ...pending, id: "b" };
  assert.deepEqual(
    reconcile([old], [pending, second], "c").map((x) => x.id),
    ["a", "b"],
  );
  const expired = {
    ...old,
    expiresAt: new Date(Date.now() - 1000).toISOString(),
  };
  assert.deepEqual(reconcile([expired], [], "c"), []);
  assert.deepEqual(applySnapshot([expired], { type: "unchanged" }), []);
});
test("pairing rejects another origin, old native QR, stale QR and strips service credentials", () => {
  const qr = {
    v: 2,
    pc: "A".repeat(64),
    nonce: "n".repeat(43),
    expires: Date.now() / 1000 + 120,
    relay: {
      url: "https://example.com/",
      id: "a".repeat(32),
      key: "k".repeat(43),
      token: "private-service-secret",
    },
  };
  const clean = validatePairing(qr, "https://example.com");
  assert.equal("token" in clean.relay, false);
  assert.throws(() => validatePairing(qr, "https://other.com"));
  assert.throws(() => validatePairing({ ...qr, v: 1 }, "https://example.com"));
  assert.throws(() =>
    validatePairing({ ...qr, expires: 0 }, "https://example.com"),
  );
});
test("password vault rejects wrong password, altered ciphertext and different conversation", async () => {
  const salt = random(16),
    key = await passwordKey("correct-password", salt),
    wrong = await passwordKey("wrong-password", salt);
  const encrypted = await seal(key, { text: "tin riêng" }, "conversation-a");
  assert.equal(JSON.stringify(encrypted).includes("tin riêng"), false);
  assert.deepEqual(await unseal(key, encrypted, "conversation-a"), {
    text: "tin riêng",
  });
  await assert.rejects(unseal(wrong, encrypted, "conversation-a"));
  await assert.rejects(unseal(key, encrypted, "conversation-b"));
  await assert.rejects(
    unseal(
      key,
      { ...encrypted, data: encrypted.data.slice(0, -5) + "AAAA" },
      "conversation-a",
    ),
  );
});
test("framing accepts split network reads and rejects oversized lengths", async () => {
  const reader = new ByteReader(),
    packet = frame(utf8("Tiếng Việt 🙂"));
  const waiting = reader.frame();
  reader.push(packet.slice(0, 2));
  reader.push(packet.slice(2, 8));
  reader.push(packet.slice(8));
  assert.equal(text(await waiting), "Tiếng Việt 🙂");
  const bad = new ByteReader();
  bad.push(new Uint8Array([127, 255, 255, 255]));
  await assert.rejects(bad.frame());
});
test("emoticons and pairing codes are deterministic", async () => {
  assert.equal(emoticons("Chào :) <3"), "Chào 🙂 ❤️");
  assert.equal(emoticons("https://site/a:)"), "https://site/a:)");
  assert.equal(
    await verificationCode("A", "B"),
    await verificationCode("B", "A"),
  );
});
test("PC deletion invalidates locked and inactive cached history without losing queued drafts", () => {
  const state = {
    conversations: [{ id: "a", historyVersion: 1 }, { id: "b" }],
    histories: { a: [{ text: "deleted" }], b: [{ text: "keep" }] },
    hashes: { a: "old", b: "keep" },
    boxes: {
      a: {
        box: "encrypted history",
        pendingBox: "encrypted pending",
        salt: "salt",
      },
    },
  };
  assert.deepEqual(
    invalidateDeletedHistory(state, [
      { id: "a", historyVersion: 2 },
      { id: "b" },
    ]),
    ["a"],
  );
  assert.equal(state.histories.a, undefined);
  assert.equal(state.hashes.a, undefined);
  assert.equal(state.boxes.a.box, undefined);
  assert.equal(state.boxes.a.pendingBox, "encrypted pending");
  assert.equal(state.histories.b[0].text, "keep");
});
