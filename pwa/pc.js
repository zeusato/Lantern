import { un64, sha, hex, concat } from "./crypto.js";

export const aiLabel = (state) => ({ idle: "Sẵn sàng", busy: "Đang làm việc", attention: "Cần phản hồi trên PC", draft: "Có bản nháp", unknown: "Chưa xác định" })[state] || "Chưa xác định";
export function childPath(parent, name) {
  if (!name || /[\\/:\x00-\x1f]/.test(name) || name === "." || name === "..") throw Error("Tên tệp không hợp lệ.");
  return parent ? `${parent}/${name}` : name;
}

// Intentionally no outbox, persistence, reconnect replay, or automatic execute retry.
export class PcClient {
  constructor(client, active = () => true) { this.client = client; this.active = active; }
  async call(pc) {
    if (!this.active() || !this.client?.ready) throw Error("Kết nối PC đã thay đổi. Mở lại mục PC.");
    const response = await this.client.request({ type: "pc", pc });
    if (!this.active()) throw Error("Phiên điều khiển đã đóng.");
    if (response.type === "error") throw Error(response.text);
    if (!response.pc) throw Error("Cập nhật Lantern trên PC để dùng chức năng này.");
    return response.pc;
  }
  prepare(command) { return this.call({ ...command, action: "prepare" }); }
  execute(ticket) { return this.call({ action: "execute", ticket }); }
}

export function openPcPanel({ getClient, available, showSheet, cleanup, esc }) {
  const connection = getClient();
  let alive = true, page = 0, root = null, path = "", ticket = "", transfer = null, objectUrl = null;
  const sheet = showSheet("Máy tính", '<div id="pc-body"></div><p id="pc-status" role="status" aria-live="polite"></p>');
  const body = sheet.querySelector("#pc-body"), status = sheet.querySelector("#pc-status");
  const active = () => alive && available() && getClient() === connection && sheet.open;
  const pc = new PcClient(connection, active);
  const note = (message) => { if (alive) status.textContent = message; };
  function release() {
    alive = false;
    if (transfer) transfer.cancelled = true;
    if (objectUrl) URL.revokeObjectURL(objectUrl);
    document.removeEventListener("visibilitychange", hidden);
  }
  function hidden() { if (document.hidden) { release(); sheet.close(); } }
  document.addEventListener("visibilitychange", hidden);
  cleanup(release);
  let busy = false;
  async function run(task) {
    if (busy || !active()) return;
    busy = true; body.querySelectorAll("button").forEach(b => { b.disabled = true; });
    try { await task(); } catch (e) { note(e.message); }
    finally { busy = false; if (active()) body.querySelectorAll("button").forEach(b => { b.disabled = b.dataset.disabled === "true"; }); }
  }
  function button(label, action, disabled = false) {
    const b = document.createElement("button"); b.className = "secondary pc-action"; b.textContent = label;
    b.dataset.disabled = String(disabled); b.disabled = disabled; b.onclick = () => run(action); body.append(b); return b;
  }
  async function home() {
    note("Đang đọc quyền PC…"); const access = await pc.call({ action: "status" });
    body.innerHTML = '<p>PC cần đang chạy và mở khóa Lantern. Quyền được cấp riêng cho điện thoại này trong Cài đặt → Điện thoại trên PC.</p>';
    button("Khóa Windows", () => confirmCommand({ command: "power.lock" }, "Khóa Windows?"), !access.power);
    button("Tắt máy", () => confirmCommand({ command: "power.shutdown" }, "Tắt PC? Ứng dụng có tài liệu chưa lưu có thể chặn tắt máy."), !access.power);
    button("AI · Hiện cửa sổ và kiểm tra", inspect, !access.ai);
    const heading = document.createElement("h3"); heading.textContent = "Tệp trên PC"; body.append(heading);
    for (const item of access.roots || []) button("📁 " + item.name, async () => { root = item; path = ""; page = 0; await browse(); });
    if (!access.roots?.length) body.insertAdjacentHTML("beforeend", "<p>Chưa được cấp thư mục nào.</p>");
    note("Đã kết nối. Lệnh không được xếp hàng khi mất mạng.");
  }
  async function confirmCommand(command, question) {
    const prepared = await pc.prepare(command); ticket = prepared.ticket;
    body.innerHTML = `<p>${esc(question)}</p><p>Lệnh hết hạn sau 45 giây. Chỉ thực hiện một lần.</p>`;
    button("Xác nhận", async () => {
      note("Đang gửi lệnh…"); const token = ticket; ticket = "";
      body.innerHTML = "";
      try { const result = await pc.execute(token); note(result.message || result.state); }
      catch { note("Chưa xác định kết quả. Không tự gửi lại; dùng nút kiểm tra bên dưới."); }
      button("Kiểm tra kết quả lệnh", async () => { const r = await pc.call({ action: "result", ticket: token }); note(r.message || r.state); });
      button("Về mục PC", home);
    });
    button("Hủy", home);
    note("");
  }
  async function inspect() {
    note("Đang hiện cửa sổ và đọc trạng thái AI trên PC…");
    const result = await pc.call({ action: "ai.inspect" }); body.innerHTML = "";
    button("‹ Về mục PC", home);
    for (const target of result.targets || []) {
      const box = document.createElement("section"); box.className = "pc-target";
      box.innerHTML = `<h3>${esc(target.app)}</h3><p>${esc(target.conversation || "Chưa đọc được cuộc trò chuyện")}</p><strong>${esc(aiLabel(target.state))}</strong><p>${esc(target.detail)}</p>`;
      body.append(box);
      if (target.state === "idle") button("Gửi yêu cầu vào " + target.app, async () => {
        body.innerHTML = `<p>${esc(target.app)} · ${esc(target.conversation)}</p><textarea id="pc-prompt" maxlength="16000" rows="5" placeholder="Yêu cầu tiếp theo…" aria-label="Yêu cầu cho AI"></textarea>`;
        button("Gửi yêu cầu", async () => {
          const prompt = body.querySelector("textarea").value.trim(); if (!prompt) throw Error("Nhập yêu cầu trước khi gửi.");
          await confirmCommand({ command: "ai.send", target: target.id, version: target.version, prompt }, `Gửi vào ${target.conversation}:\n\n${prompt}`);
        });
        button("Kiểm tra lại trạng thái", inspect); note("PC sẽ kiểm tra lại chat, trạng thái và bản nháp ngay trước khi gửi.");
      });
    }
    button("Kiểm tra lại", inspect);
    note(result.targets?.length ? "Không tự vượt bước xác nhận của AI." : "Không tìm thấy Claude hoặc ChatGPT/Codex đang chạy.");
  }
  async function browse() {
    note("Đang mở thư mục…"); const listing = await pc.call({ action: "files.list", root: root.id, path, page });
    body.innerHTML = `<p class="pc-path">${esc(root.name)}${path ? " / " + esc(path) : ""}</p>`;
    button("‹ " + (path ? "Thư mục cha" : "Về mục PC"), async () => { if (!path) return home(); path = path.split("/").slice(0, -1).join("/"); page = 0; await browse(); });
    for (const entry of listing.entries || []) button((entry.directory ? "📁 " : "📄 ") + entry.name + (entry.directory ? "" : ` · ${(entry.size / 1024).toFixed(0)} KB`), async () => {
      const next = childPath(path, entry.name);
      if (entry.directory) { path = next; page = 0; await browse(); } else await download(next);
    });
    if (page > 0) button("Trang trước", async () => { page--; await browse(); });
    if (listing.more) button("Trang tiếp", async () => { page++; await browse(); });
    note(listing.entries?.length ? "Chọn tệp để tải. Mỗi tệp tối đa 100 MB." : "Thư mục trống hoặc không có mục được phép đọc.");
  }
  async function download(filePath) {
    note("Đang chuẩn bị tệp…");
    const meta = await pc.call({ action: "files.open", root: root.id, path: filePath });
    const job = { cancelled: false }; transfer = job;
    let offset = 0; const parts = [];
    try {
    if (!Number.isSafeInteger(meta.size) || meta.size < 0 || meta.size > 100 * 1024 * 1024) throw Error("Dung lượng tệp không hợp lệ.");
    body.innerHTML = `<p>${esc(meta.name)}</p><progress max="${meta.size || 1}" value="0" aria-label="Tiến độ tải"></progress><p>Giữ Lantern mở khi tải. Tệp chỉ được giữ tạm trong phiên này.</p>`;
    const cancel = button("Hủy tải", async () => {}); cancel.onclick = () => { job.cancelled = true; note("Đang hủy tải…"); };
    cancel.disabled = false;
      while (offset < meta.size) {
        if (job.cancelled) throw Error("Đã hủy tải.");
        const r = await pc.call({ action: "files.read", ticket: meta.ticket, offset });
        if (job.cancelled) throw Error("Đã hủy tải.");
        const bytes = un64(r.data);
        if (!bytes.length || bytes.length > 262144 || r.offset !== offset || r.size !== meta.size || offset + bytes.length > meta.size) throw Error("Tệp tải về không hợp lệ.");
        parts.push(bytes); offset += bytes.length; body.querySelector("progress").value = offset;
        note(`Đã tải ${Math.round(offset / Math.max(1, meta.size) * 100)}%`);
      }
      const bytes = concat(...parts);
      if (hex(await sha(bytes)).toUpperCase() !== meta.hash?.toUpperCase()) throw Error("Checksum không khớp; chưa lưu tệp.");
      if (!active() || job.cancelled) throw Error("Phiên tải đã đóng.");
      const blob = new Blob([bytes], { type: "application/octet-stream" });
      if (objectUrl) URL.revokeObjectURL(objectUrl); objectUrl = URL.createObjectURL(blob);
      body.innerHTML = `<p>${esc(meta.name)}</p>`;
      const link = document.createElement("a"); link.className = "secondary pc-action"; link.href = objectUrl; link.download = meta.name; link.textContent = "Lưu tệp"; body.append(link);
      button("Chia sẻ / Lưu vào Tệp", async () => {
        const file = new File([blob], meta.name, { type: blob.type });
        if (navigator.canShare?.({ files: [file] })) await navigator.share({ files: [file] }); else link.click();
      });
      note("Đã tải và kiểm tra toàn vẹn tệp.");
    } finally {
      transfer = null;
      if (connection?.ready) connection.request({ type: "pc", pc: { action: "files.close", ticket: meta.ticket } }).catch(() => {});
      if (active()) button("Về thư mục", browse);
    }
  }
  run(home);
}
