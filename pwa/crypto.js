const enc = new TextEncoder();
export const utf8 = (value) => enc.encode(value);
export const text = (bytes) =>
  new TextDecoder("utf-8", { fatal: true }).decode(bytes);
export const b64 = (bytes) => {
  let s = "";
  for (const b of new Uint8Array(bytes)) s += String.fromCharCode(b);
  return btoa(s);
};
export const un64 = (value) => {
  if (typeof value !== "string" || value.length > 3000000)
    throw Error("Dữ liệu không hợp lệ");
  return Uint8Array.from(atob(value), (c) => c.charCodeAt(0));
};
export const concat = (...parts) => {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let i = 0;
  for (const p of parts) {
    out.set(p, i);
    i += p.length;
  }
  return out;
};
export const hex = (data) =>
  [...new Uint8Array(data)]
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase();
export const sha = (data) =>
  crypto.subtle.digest("SHA-256", data).then((x) => new Uint8Array(x));
export const random = (n) => crypto.getRandomValues(new Uint8Array(n));
export const uid = () => hex(random(16)).toLowerCase();
export async function createIdentity() {
  const pair = await crypto.subtle.generateKey(
    { name: "ECDSA", namedCurve: "P-256" },
    true,
    ["sign", "verify"],
  );
  const key = b64(await crypto.subtle.exportKey("spki", pair.publicKey));
  return {
    key,
    privateKey: await crypto.subtle.exportKey("jwk", pair.privateKey),
    id: hex(await sha(un64(key))),
  };
}
export async function verificationCode(pc, device) {
  const h = hex(
    await sha(utf8("Lantern-pair-v1:" + [pc, device].sort().join(":"))),
  );
  return h.slice(0, 24).match(/.{4}/g).join(" ");
}
export function frame(bytes) {
  const head = new Uint8Array(4);
  new DataView(head.buffer).setUint32(0, bytes.length);
  return concat(head, bytes);
}
export class ByteReader {
  constructor() {
    this.chunks = [];
    this.length = 0;
    this.waiter = null;
    this.error = null;
  }
  push(bytes) {
    if (this.error) return;
    if (this.length + bytes.length > 4 * 1024 * 1024)
      return this.close(Error("Kênh nhận quá tải"));
    this.chunks.push(bytes);
    this.length += bytes.length;
    this.wake();
  }
  wake() {
    this.waiter?.();
    this.waiter = null;
  }
  close(error = Error("Kết nối đã đóng")) {
    this.error = error;
    this.chunks = [];
    this.length = 0;
    this.wake();
  }
  async read(n) {
    while (this.length < n) {
      if (this.error) throw this.error;
      await new Promise((r) => (this.waiter = r));
    }
    const out = new Uint8Array(n);
    let at = 0;
    while (at < n) {
      const c = this.chunks[0],
        take = Math.min(c.length, n - at);
      out.set(c.subarray(0, take), at);
      at += take;
      this.length -= take;
      if (take === c.length) this.chunks.shift();
      else this.chunks[0] = c.subarray(take);
    }
    return out;
  }
  async frame(max = 2 * 1024 * 1024) {
    const head = await this.read(4),
      n = new DataView(head.buffer).getUint32(0);
    if (n < 1 || n > max) throw Error("Kích thước gói không hợp lệ");
    return this.read(n);
  }
}
export async function secureChannel(transport, identity, pairing) {
  const own = await crypto.subtle.importKey(
    "jwk",
    identity.privateKey,
    { name: "ECDSA", namedCurve: "P-256" },
    false,
    ["sign"],
  );
  const eph = await crypto.subtle.generateKey(
    { name: "ECDH", namedCurve: "P-256" },
    true,
    ["deriveBits"],
  );
  const hello = {
    key: identity.key,
    ephemeral: b64(await crypto.subtle.exportKey("spki", eph.publicKey)),
    random: b64(random(32)),
  };
  const clientText = [
    "Lantern-web-client-v1",
    pairing.pc,
    pairing.relay.id,
    transport.session,
    hello.key,
    hello.ephemeral,
    hello.random,
  ].join("\n");
  hello.signature = b64(
    await crypto.subtle.sign(
      { name: "ECDSA", hash: "SHA-256" },
      own,
      utf8(clientText),
    ),
  );
  await transport.write(frame(utf8(JSON.stringify(hello))));
  const server = JSON.parse(text(await transport.reader.frame(4096)));
  if (
    hex(await sha(un64(server.key))) !== pairing.pc ||
    un64(server.random).length !== 32
  )
    throw Error("Danh tính PC không khớp mã QR");
  const signKey = await crypto.subtle.importKey(
    "spki",
    un64(server.key),
    { name: "ECDSA", namedCurve: "P-256" },
    false,
    ["verify"],
  );
  const transcript = utf8(
    [
      "Lantern-web-server-v1",
      clientText,
      server.key,
      server.ephemeral,
      server.random,
    ].join("\n"),
  );
  if (
    !(await crypto.subtle.verify(
      { name: "ECDSA", hash: "SHA-256" },
      signKey,
      un64(server.signature),
      transcript,
    ))
  )
    throw Error("Không xác thực được PC");
  const remote = await crypto.subtle.importKey(
    "spki",
    un64(server.ephemeral),
    { name: "ECDH", namedCurve: "P-256" },
    false,
    [],
  );
  const shared = await crypto.subtle.deriveBits(
    { name: "ECDH", public: remote },
    eph.privateKey,
    256,
  );
  const context = await sha(transcript),
    hkdf = await crypto.subtle.importKey("raw", shared, "HKDF", false, [
      "deriveBits",
    ]);
  const keys = new Uint8Array(
    await crypto.subtle.deriveBits(
      {
        name: "HKDF",
        hash: "SHA-256",
        salt: context,
        info: utf8("Lantern-web-keys-v1"),
      },
      hkdf,
      512,
    ),
  );
  const sendKey = await crypto.subtle.importKey(
    "raw",
    keys.slice(0, 32),
    "AES-GCM",
    false,
    ["encrypt"],
  );
  const recvKey = await crypto.subtle.importKey(
    "raw",
    keys.slice(32),
    "AES-GCM",
    false,
    ["decrypt"],
  );
  keys.fill(0);
  new Uint8Array(shared).fill(0);
  let sent = 0n,
    received = 0n,
    writeTail = Promise.resolve();
  const plain = new ByteReader();
  const nonce = (n) => {
    const b = new Uint8Array(12);
    new DataView(b.buffer).setBigUint64(4, n);
    return b;
  };
  const aad = (direction, n) => {
    const b = new Uint8Array(9);
    b[0] = direction;
    new DataView(b.buffer).setBigUint64(1, n);
    return concat(context, b);
  };
  const pump = (async () => {
    try {
      while (!transport.closed) {
        const record = await transport.reader.frame(32784);
        const n = ++received;
        plain.push(
          new Uint8Array(
            await crypto.subtle.decrypt(
              {
                name: "AES-GCM",
                iv: nonce(n),
                additionalData: aad(1, n),
                tagLength: 128,
              },
              recvKey,
              record,
            ),
          ),
        );
      }
    } catch (e) {
      plain.close(e);
      transport.close(e);
    }
  })();
  return {
    async read() {
      return JSON.parse(text(await plain.frame()));
    },
    write(value) {
      const work = writeTail.then(async () => {
        const bytes = frame(utf8(JSON.stringify(value)));
        if (bytes.length > 2 * 1024 * 1024 + 4) throw Error("Nội dung quá lớn");
        for (let i = 0; i < bytes.length; i += 32768) {
          const n = ++sent;
          const record = await crypto.subtle.encrypt(
            {
              name: "AES-GCM",
              iv: nonce(n),
              additionalData: aad(0, n),
              tagLength: 128,
            },
            sendKey,
            bytes.subarray(i, i + 32768),
          );
          await transport.write(frame(new Uint8Array(record)));
        }
      });
      writeTail = work.catch(() => {});
      return work;
    },
    close() {
      plain.close();
      transport.close();
    },
    pump,
  };
}
