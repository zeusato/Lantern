import { ByteReader, b64, un64, uid, secureChannel } from "./crypto.js";
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
export function validatePairing(value, origin) {
  let p;
  try { p = typeof value === "string" ? JSON.parse(value) : value; }
  catch { throw Error("Đã đọc được QR nhưng đây không phải mã Lantern. Trên PC chọn Ghép đôi iPhone · Bản web."); }
  if (p?.v === 1) throw Error("Đây là mã cho app Android/iOS. Trên PC chọn Ghép đôi iPhone · Bản web để tạo đúng mã.");
  if (p?.v === 2 && typeof p.expires === "number" && p.expires * 1000 < Date.now())
    throw Error("Đã đọc được QR nhưng mã đã hết hạn. Tạo mã mới trên PC rồi quét lại.");
  if (
    p?.v !== 2 ||
    !/^[A-F0-9]{64}$/.test(p.pc) ||
    !/^[a-f0-9]{32}$/.test(p.relay?.id) ||
    !/^[\w-]{43}$/.test(p.relay?.key) ||
    !/^[\w-]{43}$/.test(p.nonce) ||
    typeof p.expires !== "number" ||
    p.expires * 1000 < Date.now()
  )
    throw Error(
      "Mã QR không hợp lệ hoặc đã hết hạn. Tạo mã “Bản web” trên PC.",
    );
  if (new URL(p.relay.url).origin !== origin)
    throw Error(
      "Mã QR thuộc relay khác. Mở bản web tại địa chỉ hiển thị trên PC.",
    );
  return {
    v: 2,
    pc: p.pc,
    name: String(p.name || "PC").slice(0, 80),
    nonce: p.nonce,
    expires: p.expires,
    relay: { id: p.relay.id, key: p.relay.key },
  };
}
export class RelayTransport {
  constructor(pairing, fetcher = (url, options) => fetch(url, options)) {
    this.pairing = pairing;
    this.fetcher = fetcher;
    this.session = uid();
    this.seq = 0;
    this.after = 0;
    this.reader = new ByteReader();
    this.abort = new AbortController();
    this.closed = false;
    this.writeTail = Promise.resolve();
  }
  async call(action, body) {
    const timeout = new AbortController(),
      timer = setTimeout(() => timeout.abort(), 12000),
      stop = () => timeout.abort();
    this.abort.signal.addEventListener("abort", stop, { once: true });
    let response;
    try {
      response = await this.fetcher(
        "/v1/routes/" + this.pairing.relay.id + "/" + action,
        {
          method: "POST",
          credentials: "same-origin",
          cache: "no-store",
          redirect: "error",
          headers: {
            "Content-Type": "application/json",
            Authorization: "Bearer " + this.pairing.relay.key,
          },
          body: JSON.stringify(body),
          signal: timeout.signal,
        },
      );
    } finally {
      clearTimeout(timer);
      this.abort.signal.removeEventListener("abort", stop);
    }
    if (!response.ok) {
      const e = Error(
        response.status === 401 || response.status === 403
          ? "Phiên truy cập đã hết hạn. Mở lại trang và đăng nhập."
          : response.status === 404
            ? "PC chưa sẵn sàng hoặc thiết bị đã bị gỡ."
            : response.status === 409
              ? "Kết nối đã đổi. Đang nối lại."
              : "Không kết nối được với PC.",
      );
      e.status = response.status;
      throw e;
    }
    return response.json();
  }
  async open() {
    await this.call("open", { session: this.session });
    this.pump = this.receive();
    return this;
  }
  async receive() {
    let failedAt = 0;
    try {
      while (!this.closed) {
        try {
          const data = await this.call("exchange", {
            session: this.session,
            after: this.after,
            batch: 4,
          });
          failedAt = 0;
          for (const p of data.packets || []) {
            if (p.seq !== this.after + 1) throw Error("Sai thứ tự gói tin");
            this.reader.push(un64(p.data));
            this.after = p.seq;
          }
          if (!data.packets?.length) await sleep(60);
        } catch (e) {
          if (this.closed || [401, 403, 404, 409].includes(e.status)) throw e;
          failedAt ||= Date.now();
          if (Date.now() - failedAt > 12000) throw e;
          await sleep(350);
        }
      }
    } catch (e) {
      this.close(e);
    }
  }
  write(bytes) {
    const work = this.writeTail.then(async () => {
      for (let i = 0; i < bytes.length; i += 49152) {
        const data = b64(bytes.subarray(i, i + 49152)),
          seq = ++this.seq,
          start = Date.now();
        while (true) {
          if (this.closed) throw Error("Đã ngắt kết nối");
          try {
            await this.call("exchange", {
              session: this.session,
              after: this.after,
              seq,
              data,
              pushOnly: true,
            });
            break;
          } catch (e) {
            if (
              [401, 403, 404, 409].includes(e.status) ||
              Date.now() - start > 12000
            )
              throw e;
            await sleep(200);
          }
        }
      }
    });
    this.writeTail = work.catch((e) => {
      this.close(e);
    });
    return work;
  }
  close(error = Error("Đã ngắt kết nối")) {
    if (this.closed) return;
    this.closed = true;
    this.abort.abort();
    this.reader.close(error);
  }
}
export class Client {
  constructor(identity, pairing, onUpdate, onState) {
    this.identity = identity;
    this.pairing = pairing;
    this.onUpdate = onUpdate;
    this.onState = onState;
    this.pending = new Map();
    this.stopped = false;
    this.ready = false;
    this.attempt = 0;
  }
  async connect() {
    const attempt = ++this.attempt;
    this.ready = false;
    this.onState("connecting");
    this.transport?.close();
    this.transport = new RelayTransport(this.pairing);
    const timer = setTimeout(
      () =>
        this.transport?.close(
          Error("PC chưa phản hồi. Kiểm tra Lantern trên PC đã mở khóa."),
        ),
      30000,
    );
    try {
      await this.transport.open();
      this.channel = await secureChannel(
        this.transport,
        this.identity,
        this.pairing,
      );
      this.onState("approving");
      await this.channel.write({
        type: "hello",
        nonce: this.pairing.nonce || "",
        name: "iPhone · Lantern Web",
        capabilities: ["sync-v1", "push-v1"],
      });
      clearTimeout(timer);
      const approvalTimer = setTimeout(() => this.channel?.close(), 95000);
      let hello;
      try {
        hello = await this.channel.read();
      } finally {
        clearTimeout(approvalTimer);
      }
      if (hello.type !== "welcome" || hello.id !== this.pairing.pc)
        throw Error("PC chưa cho phép thiết bị");
      if (this.stopped || attempt !== this.attempt) {
        this.channel.close();
        return;
      }
      this.ready = true;
      this.onState("online", hello);
      this.heartbeat = setInterval(() => {
        if (this.pending.size === 0)
          this.request({ type: "list" }).catch(() => {});
      }, 25000);
      while (!this.stopped && attempt === this.attempt) {
        const message = await this.channel.read();
        if (this.stopped || attempt !== this.attempt) break;
        const pending = this.pending.get(message.id);
        if (pending) {
          clearTimeout(pending.timer);
          this.pending.delete(message.id);
          message.type === "error"
            ? pending.reject(Error(message.text))
            : pending.resolve(message);
        } else this.onUpdate(message);
      }
    } catch (e) {
      if (!this.stopped && attempt === this.attempt) this.onState("offline", e);
    } finally {
      clearTimeout(timer);
      clearInterval(this.heartbeat);
      this.ready = false;
      for (const p of this.pending.values()) {
        clearTimeout(p.timer);
        p.reject(Error("Mất kết nối. Tin nhắn vẫn được giữ trên máy."));
      }
      this.pending.clear();
      this.transport?.close();
      if (!this.stopped && attempt === this.attempt)
        this.retry = setTimeout(() => this.connect(), 1500);
    }
  }
  request(packet, timeout = 20000) {
    if (!this.ready) return Promise.reject(Error("Đang chờ kết nối PC"));
    packet = { ...packet, id: packet.id || uid() };
    if (this.pending.has(packet.id)) return this.pending.get(packet.id).promise;
    let resolve, reject;
    const promise = new Promise((yes, no) => {
      resolve = yes;
      reject = no;
    });
    const timer = setTimeout(() => {
      this.pending.delete(packet.id);
      reject(Error("PC phản hồi chậm. Tin vẫn được giữ để thử lại."));
    }, timeout);
    this.pending.set(packet.id, { resolve, reject, timer, promise });
    this.channel.write(packet).catch((e) => {
      clearTimeout(timer);
      this.pending.delete(packet.id);
      reject(e);
    });
    return promise;
  }
  stop() {
    this.stopped = true;
    ++this.attempt;
    clearTimeout(this.retry);
    clearInterval(this.heartbeat);
    this.channel?.close();
    this.transport?.close();
  }
}
