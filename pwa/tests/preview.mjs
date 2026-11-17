import http from "node:http";
import net from "node:net";
import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
const root = resolve(import.meta.dirname, ".."),
  metadata = JSON.parse(await readFile(process.argv[2], "utf8"));
let active;
const routes = new Map();
const write = (res, data, status = 200, type = "application/json") => {
  res.writeHead(status, { "Content-Type": type, "Cache-Control": "no-store" });
  res.end(type === "application/json" ? JSON.stringify(data) : data);
};
const packet = (value) => {
  const data = Buffer.from(JSON.stringify(value)),
    head = Buffer.alloc(4);
  head.writeUInt32BE(data.length);
  return Buffer.concat([head, data]);
};
const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, "http://localhost:5180");
    if (url.pathname === "/fixture") {
      return write(res, metadata);
    }
    if (
      url.pathname === "/fixture/message" ||
      url.pathname === "/fixture/lock"
    ) {
      let raw = "";
      for await (const b of req) raw += b;
      const body = JSON.parse(raw || "{}");
      const socket = net.connect(metadata.port, "127.0.0.1", () =>
        socket.write(
          packet({
            type: url.pathname.endsWith("lock")
              ? "fixture-lock"
              : "fixture-message",
            text: body.text || "Tin nhắn mới từ PC 🙂",
          }),
        ),
      );
      socket.once("data", () => {
        socket.destroy();
        write(res, { ok: true });
      });
      socket.on("error", () => write(res, { error: "fixture" }, 500));
      return;
    }
    const match = url.pathname.match(
      /^\/v1\/routes\/([a-f0-9]{32})\/(open|exchange)$/,
    );
    if (match) {
      if (
        match[1] !== metadata.qr.relay.id ||
        req.headers.authorization !== "Bearer " + metadata.qr.relay.key
      )
        return write(res, { error: "unauthorized" }, 401);
      let raw = "";
      for await (const b of req) raw += b;
      if (raw.length > 90000) return write(res, {}, 413);
      const body = JSON.parse(raw),
        action = match[2];
      if (action === "open") {
        active?.socket.destroy();
        const socket = net.connect(metadata.port, "127.0.0.1");
        active = {
          socket,
          session: body.session,
          inSeq: 0,
          outSeq: 0,
          queue: [],
          sent: new Map(),
        };
        const state = active;
        routes.set(body.session, state);
        socket.on("connect", () =>
          socket.write(packet({ type: "web-open", id: body.session })),
        );
        socket.on("data", (data) => {
          for (let i = 0; i < data.length; i += 49152)
            state.queue.push({
              seq: ++state.outSeq,
              data: data.subarray(i, i + 49152).toString("base64"),
            });
        });
        socket.on("error", () => {});
        return write(res, { session: body.session });
      }
      const state = routes.get(body.session);
      if (!state || active !== state)
        return write(res, { error: "session_changed" }, 409);
      state.queue = state.queue.filter((p) => p.seq > body.after);
      if (body.data) {
        if (state.sent.has(body.seq)) {
          if (state.sent.get(body.seq) !== body.data)
            return write(res, {}, 409);
        } else {
          if (body.seq !== state.inSeq + 1) return write(res, {}, 409);
          state.inSeq = body.seq;
          state.sent.set(body.seq, body.data);
          state.socket.write(Buffer.from(body.data, "base64"));
        }
      }
      return write(res, {
        session: body.session,
        packets: body.pushOnly ? [] : state.queue.slice(0, 4),
      });
    }
    const path = url.pathname === "/" ? "index.html" : url.pathname.slice(1),
      allowed = [
        "index.html",
        "app.js",
        "style.css",
        "crypto.js",
        "channel.js",
        "model.js",
        "qr-scanner.js", "calculator.js", "camouflage.js", "calculator.css",
        "vault.js",
        "sw.js",
        "manifest.webmanifest",
        "icon.svg",
        "icon-180.png", "icon-192.png",
        "icon-512.png",
      ];
    if (path === "vendor/jsQR.js")
      return write(
        res,
        await readFile(resolve(root, "node_modules/jsqr/dist/jsQR.js")),
        200,
        "text/javascript",
      );
    if (!allowed.includes(path)) return write(res, {}, 404);
    const types = {
      js: "text/javascript",
      css: "text/css",
      html: "text/html",
      svg: "image/svg+xml",
      png: "image/png",
      webmanifest: "application/manifest+json",
    };
    write(
      res,
      await readFile(resolve(root, path)),
      200,
      types[path.split(".").at(-1)],
    );
  } catch (e) {
    write(res, { error: e.message }, 500);
  }
});
server.listen(5180, "127.0.0.1", () =>
  console.log("Local: http://localhost:5180 — synthetic PC fixture only"),
);
