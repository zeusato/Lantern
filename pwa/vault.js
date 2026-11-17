import { random, b64, un64, utf8, text } from "./crypto.js";
export async function passwordKey(password, salt) {
  const seed = await crypto.subtle.importKey(
    "raw",
    utf8(password),
    "PBKDF2",
    false,
    ["deriveKey"],
  );
  return crypto.subtle.deriveKey(
    { name: "PBKDF2", hash: "SHA-256", salt, iterations: 310000 },
    seed,
    { name: "AES-GCM", length: 256 },
    false,
    ["encrypt", "decrypt"],
  );
}
export async function seal(key, value, purpose = "Lantern-web-vault-v1") {
  const iv = random(12);
  return {
    iv: b64(iv),
    data: b64(
      await crypto.subtle.encrypt(
        { name: "AES-GCM", iv, additionalData: utf8(purpose) },
        key,
        utf8(JSON.stringify(value)),
      ),
    ),
  };
}
export async function unseal(key, value, purpose = "Lantern-web-vault-v1") {
  return JSON.parse(
    text(
      await crypto.subtle.decrypt(
        { name: "AES-GCM", iv: un64(value.iv), additionalData: utf8(purpose) },
        key,
        un64(value.data),
      ),
    ),
  );
}
export class Vault {
  async init() {
    this.db = await new Promise((resolve, reject) => {
      const request = indexedDB.open("lantern-mobile-v1", 1);
      request.onupgradeneeded = () => request.result.createObjectStore("vault");
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    this.tail = Promise.resolve();
    return this;
  }
  async get(name) {
    return new Promise((resolve, reject) => {
      const req = this.db.transaction("vault").objectStore("vault").get(name);
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }
  async put(name, value) {
    return new Promise((resolve, reject) => {
      const tx = this.db.transaction("vault", "readwrite");
      tx.objectStore("vault").put(value, name);
      tx.oncomplete = resolve;
      tx.onerror = () => reject(tx.error);
    });
  }
  async create(password, data) {
    const salt = random(16);
    this.key = await passwordKey(password, salt);
    await this.put("header", {
      salt: b64(salt),
      check: await seal(this.key, { valid: true }),
    });
    await this.save(data);
  }
  async unlock(password) {
    const h = await this.get("header");
    const key = await passwordKey(password, un64(h.salt));
    await unseal(key, h.check);
    const data = await this.get("state");
    const value = data ? await unseal(key, data) : {};
    this.key = key;
    return value;
  }
  save(data) {
    const key = this.key,
      copy = structuredClone(data);
    if (!key) return Promise.reject(Error("Ứng dụng đang khóa"));
    const work = this.tail.then(async () =>
      this.put("state", await seal(key, copy)),
    );
    this.tail = work.catch(() => {});
    return work;
  }
  lock() {
    this.key = null;
  }
  async reset() {
    await this.tail;
    await new Promise((resolve, reject) => {
      const tx = this.db.transaction("vault", "readwrite");
      tx.objectStore("vault").clear();
      tx.oncomplete = resolve;
      tx.onerror = () => reject(tx.error);
    });
    this.lock();
  }
}
