import test from "node:test";
import assert from "node:assert/strict";
import { PcClient, childPath, aiLabel } from "../pc.js";

test("PC commands never enter a retry/outbox loop after an ambiguous failure", async () => {
  const sent = [];
  const client = new PcClient({ ready: true, request: async p => { sent.push(p); if (p.pc.action === "prepare") return { pc: { ticket: "one" } }; throw Error("disconnected"); } });
  const prepared = await client.prepare({ command: "power.shutdown" });
  await assert.rejects(client.execute(prepared.ticket), /disconnected/);
  assert.deepEqual(sent.map(p => p.pc.action), ["prepare", "execute"]);
});
test("closing or changing the connection prevents a later execute", async () => {
  let active = true, sends = 0;
  const client = new PcClient({ ready: true, request: async () => { sends++; return { pc: { ticket: "one" } }; } }, () => active);
  await client.prepare({ command: "ai.send" }); active = false;
  await assert.rejects(client.execute("one"), /thay đổi/); assert.equal(sends, 1);
});
test("offline PC actions are rejected immediately", async () => {
  let sends = 0; const client = new PcClient({ ready: false, request: () => sends++ });
  await assert.rejects(client.execute("old"), /thay đổi/); assert.equal(sends, 0);
});
test("late responses cannot repopulate a closed control panel", async () => {
  let active = true; const client = new PcClient({ ready: true, request: async () => { active = false; return { pc: { targets: [] } }; } }, () => active);
  await assert.rejects(client.call({ action: "ai.inspect" }), /đã đóng/);
});
test("browser tree paths cannot turn a filename into traversal", () => {
  assert.equal(childPath("hồ sơ", "tài liệu.pdf"), "hồ sơ/tài liệu.pdf");
  for (const bad of ["..", ".", "../secret", "a\\b", "C:secret", "bad\0name"]) assert.throws(() => childPath("root", bad));
  assert.notEqual(aiLabel("unknown"), aiLabel("idle"));
});
