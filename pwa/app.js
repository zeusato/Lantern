import {
  createIdentity,
  verificationCode,
  uid,
  utf8,
  sha,
  hex,
  b64,
  un64,
  concat,
  random,
} from "./crypto.js";
import { Client, validatePairing } from "./channel.js";
import { openPcPanel } from "./pc.js";
import { Vault, passwordKey, seal, unseal } from "./vault.js";
import { CameraScanner, readQrPhoto } from "./qr-scanner.js";
import { isCalculator, showCalculator, initializeCalculator, calculatorSetupComplete, showNotice as toast } from "./camouflage.js";
import { hasCalculatorLock, validateCalculatorExpression, createCalculatorLock, saveCalculatorLock, matchesCalculatorLock } from "./calculator-lock.js";
import {
  joins,
  alive,
  reconcile,
  applySnapshot,
  emoticons,
  invalidateDeletedHistory,
} from "./model.js";
async function boot() {
  try {
    vault = await new Vault().init();
    initializeCalculator();
    await gate();
    if ("serviceWorker" in navigator) {
      const registration = await navigator.serviceWorker.register("/sw.js");
      const update = () => {
        if (!registration.waiting) return;
        toast("Có phiên bản mới. Mở Cài đặt để cập nhật.");
        window.lanternUpdate = registration;
      };
      update();
      registration.addEventListener("updatefound", () =>
        registration.installing?.addEventListener("statechange", update),
      );
      navigator.serviceWorker.addEventListener("controllerchange", () =>
        location.reload(),
      );
    }
  } catch {
    app.innerHTML =
      '<main class="gate"><h1>Không mở được Lantern</h1><p>Hãy dùng Safari ngoài chế độ riêng tư, cho phép lưu dữ liệu trang web rồi thử lại.</p></main>';
  }
}
queueMicrotask(boot);
function connect() {
  client?.stop();
  if (!state?.pairing) return;
  lastServerSerial = 0;
  const instance = new Client(
    state.identity,
    state.pairing,
    (p) => {
      if (client === instance) update(p);
    },
    (next, detail) => {
      if (client !== instance) return;
      connection = next;
      connectText = detail?.message || "";
      renderStatus();
      if (sheet.dataset.kind === "pairing") {
        let status = sheet.querySelector("[data-connection]");
        if (!status) {
          status = document.createElement("p");
          status.dataset.connection = "";
          sheet.querySelector(".sheet-body").append(status);
        }
        status.textContent =
          next === "offline"
            ? connectText
            : next === "approving"
              ? "Đang chờ PC xác nhận…"
              : next === "connecting"
                ? "Đang kết nối an toàn…"
                : "";
      }
      if (next === "online") {
        state.name = detail.name;
        state.pairing.nonce = "";
        saveSoon();
        closePairingSheet();
        (async () => {
          restoring = true;
          try {
            for (const [id, password] of sessionPasswords) {
              try {
                update(
                  await client.request({
                    type: "unlock",
                    conversation: id,
                    password,
                  }),
                );
              } catch {
                lockLocal(id);
              }
            }
          } finally {
            restoring = false;
          }
          await sync();
          flushOutbox();
        })().catch(() => {});
      }
      if (next === "offline") {
        for (const m of state.outbox) m._failed = true;
        renderMessages();
      }
    },
  );
  client = instance;
  instance.connect();
}
function closePairingSheet() {
  if (sheet.dataset.kind === "pairing") {
    delete sheet.dataset.kind;
    closeSheet();
  }
}
function update(packet) {
  if (!state) return;
  const wasLocked = current ? isLocked(current) : true;
  if (packet.serial && packet.serial <= lastServerSerial) return;
  if (packet.serial) lastServerSerial = packet.serial;
  if (
    packet.conversations?.length ||
    (packet.type === "ok" && packet.conversation === "" && packet.conversations)
  ) {
    const oldUnread = state.conversations.reduce(
      (n, c) => n + (c.unread || 0),
      0,
    );
    for (const id of invalidateDeletedHistory(state, packet.conversations))
      clearMedia(id);
    state.conversations = packet.conversations;
    if (
      state.settings.inAppNotice &&
      !document.hidden &&
      state.conversations.reduce((n, c) => n + (c.unread || 0), 0) >
        oldUnread &&
      !current
    )
      toast("Có tin nhắn mới");
    for (const c of state.conversations) {
      if (c.protected && c.locked && !restoring && (sessionKeys.has(c.id) || c.id === current && !wasLocked))
        lockLocal(c.id);
    }
  }
  if (
    packet.conversation &&
    ["ok", "unchanged"].includes(packet.type) &&
    packet.hash
  ) {
    const id = packet.conversation;
    if (!isLocked(id)) {
      state.histories[id] = applySnapshot(history(id), packet);
      state.hashes ||= {};
      state.hashes[id] = packet.hash;
      const ids = new Set(state.histories[id].map((m) => m.id));
      state.outbox = state.outbox.filter((m) => !ids.has(m.id));
    }
  }
  if (
    packet.locked &&
    packet.conversation &&
    sessionKeys.has(packet.conversation) &&
    !restoring
  )
    lockLocal(packet.conversation);
  renderList();
  renderMessages();
  renderStatus();
  saveSoon();
}
async function sync() {
  if (!client?.ready || syncBusy || document.hidden) return;
  syncBusy = true;
  try {
    const id = current && !isLocked(current) ? current : "";
    update(
      await client.request({
        type: "sync",
        conversation: id,
        hash: state.hashes?.[id] || "",
        visibleIds: visibleIds(),
      }),
    );
  } catch {
  } finally {
    syncBusy = false;
  }
}
function visibleIds() {
  if (isCalculator() || !current || isLocked(current) || document.hidden || !$("#timeline"))
    return [];
  const r = $("#timeline").getBoundingClientRect();
  return [...$("#timeline").querySelectorAll("[data-message]")]
    .filter((el) => {
      const b = el.getBoundingClientRect();
      return b.bottom > r.top && b.top < r.bottom;
    })
    .map((el) => el.dataset.message)
    .filter((id) => history(current).some((m) => m.id === id && !m.outgoing))
    .slice(-100);
}
function markRead() {
  clearTimeout(markRead.timer);
  if (isCalculator() || !client?.ready || !current || isLocked(current) || document.hidden)
    return;
  markRead.timer = setTimeout(() => {
    const ids = visibleIds();
    if (!ids.length) return;
    client
      .request({
        type: "sync",
        conversation: current,
        hash: state.hashes?.[current] || "",
        visibleIds: ids,
      })
      .then(update)
      .catch(() => {});
  }, 450);
}
async function sendText(event) {
  event.preventDefault();
  const input = $("#draft"),
    raw = input.value.trim(),
    id = current,
    quoted = replyTo?.id || "",
    owner = state;
  if (!raw || isLocked(id)) return;
  if (raw.length > 16000) {
    toast("Mỗi tin nhắn tối đa 16.000 ký tự.");
    return;
  }
  input.value = "";
  state.drafts[id] = "";
  replyTo = null;
  $("#reply-banner").hidden = true;
  autosize();
  input.focus({ preventScroll: true });
  const requestId = uid(),
    messageId = hex(await sha(utf8(state.identity.id + requestId)))
      .slice(0, 32)
      .toLowerCase();
  if (state !== owner || isLocked(id)) return;
  const message = {
    id: messageId,
    _requestId: requestId,
    _pending: true,
    conversationId: id,
    senderId: state.pairing?.pc || state.identity.id,
    senderName: state.name || "Bạn",
    outgoing: true,
    time: new Date().toISOString(),
    text: state.settings.emoticons ? emoticons(raw) : raw,
    kind: "text",
    replyToId: quoted,
    status: "queued",
  };
  state.outbox.push(message);
  lastRender = "";
  renderMessages(true);
  renderList();
  try {
    await save();
    flushOutbox();
  } catch {
    message._failed = true;
    renderMessages();
    toast("Chưa lưu được tin trên máy. Hãy kiểm tra dung lượng.");
  }
}
async function flushOutbox() {
  if (sending || !client?.ready || document.hidden) return;
  sending = true;
  const owner = state;
  try {
    await save();
    if (state !== owner) return;
    for (const m of [...state.outbox]) {
      if (state !== owner || !client?.ready || document.hidden) break;
      if (isLocked(m.conversationId) || m._upload) continue;
      m._failed = false;
      renderMessages();
      try {
        const result = await client.request({
          type: "send",
          id: m._requestId,
          conversation: m.conversationId,
          text: m.text,
          replyTo: m.replyToId || "",
        });
        if (state !== owner) break;
        state.outbox = state.outbox.filter((x) => x.id !== m.id);
        if (result.type !== "gone" && result.messages?.length) {
          const old = history(m.conversationId),
            ids = new Set(result.messages.map((x) => x.id));
          state.histories[m.conversationId] = [
            ...old.filter((x) => !ids.has(x.id)),
            ...result.messages,
          ];
        }
        renderMessages();
        renderList();
        await save();
      } catch {
        m._failed = true;
        renderMessages();
        break;
      }
    }
  } catch {
    toast("Chưa lưu được tin. Kiểm tra dung lượng điện thoại.");
  } finally {
    sending = false;
  }
}
function pairSheet() {
  showSheet(
    "Ghép đôi với PC",
    `<p class="pair-help">Trên PC: Cài đặt → Điện thoại → <strong>Ghép đôi iPhone · Bản web</strong>.</p><div id="scan-area" class="scan-area"><div class="scan-placeholder">${icon("scan")}<span>Quét mã trên màn hình PC</span><button id="scan" class="primary">Mở camera</button></div><video class="scan-video" autoplay playsinline webkit-playsinline muted aria-label="Camera quét QR" hidden></video><div class="scan-guide" aria-hidden="true" hidden></div></div><p id="scan-status" class="scan-status" role="status">PC cần mở khóa Lantern. Mã QR có hiệu lực 2 phút.</p><div class="scan-actions"><button id="scan-stop" class="text-action" hidden>Dừng camera</button><button id="qr-photo-button" class="secondary">Chọn ảnh QR</button><input id="qr-photo" type="file" accept="image/*" hidden></div><p class="error" id="pair-error" role="alert"></p><details><summary>Dán nội dung mã QR</summary><form id="pair-form"><textarea id="pair-text" rows="4" placeholder="Nội dung QR bản web" aria-label="Nội dung QR"></textarea><button class="secondary">Kết nối PC</button></form></details>`,
  );
  $("#scan").onclick = startScan;
  const photo = $("#qr-photo"), photoButton = $("#qr-photo-button");
  photoButton.onclick = () => photo.click();
  photo.onchange = async () => {
    const file = photo.files[0];
    if (!file || !photo.isConnected) return;
    sheetCleanup?.(); sheetCleanup = null;
    photoButton.disabled = true;
    $("#scan-status").textContent = "Đang đọc ảnh QR…";
    $("#pair-error").textContent = "";
    try {
      const value = await readQrPhoto(file);
      if (photo.isConnected) await pairValue(value);
    } catch (error) {
      if (photo.isConnected) $("#pair-error").textContent = error.message;
    } finally {
      if (photo.isConnected) {
        photoButton.disabled = false; photo.value = "";
        $("#scan-status").textContent = "Chọn ảnh khác hoặc mở camera để thử lại.";
      }
    }
  };
  $("#pair-form").onsubmit = (e) => {
    e.preventDefault();
    pairValue($("#pair-text").value);
  };
}
async function pairValue(value) {
  const owner = state, pairingForm = $("#pair-form");
  if (!pairingForm?.isConnected || pairingForm.dataset.busy) return false;
  pairingForm.dataset.busy = "true";
  try {
    const pairing = validatePairing(value, location.origin);
    if (!pairingForm?.isConnected) return false;
    $("#scan-status").textContent = "Đã nhận mã QR · Đang chuẩn bị kết nối…";
    const previousPairing = state.pairing;
    state.pairing = pairing;
    try { await save(); } catch (error) { if (state === owner) state.pairing = previousPairing; throw error; }
    if (state !== owner || !pairingForm.isConnected) return false;
    sheetCleanup?.();
    sheetCleanup = null;
    const code = await verificationCode(pairing.pc, state.identity.id);
    showSheet(
      "Xác nhận trên PC",
      `<p>Đang kết nối <strong>${esc(pairing.name)}</strong>. Khi PC hỏi, đối chiếu mã dưới đây rồi chọn <strong>Cho phép thiết bị</strong>.</p><div class="code">${code}</div><p>Giữ màn hình này mở trong lúc chờ. Mỗi mã QR chỉ dùng một lần.</p><button id="pair-cancel" class="secondary">Hủy ghép đôi</button>`,
    );
    sheet.dataset.kind = "pairing";
    $("#pair-cancel").onclick = () => {
      client?.stop();
      state.pairing = null;
      saveSoon();
      closeSheet();
    };
    connect();
    return true;
  } catch (e) {
    if ($("#pair-error")) $("#pair-error").textContent = e.message;
    else toast(e.message);
    return false;
  } finally {
    delete pairingForm.dataset.busy;
  }
}
async function startScan() {
  sheetCleanup?.();
  const area = $("#scan-area");
  if (!area) return;
  const video = area.querySelector("video"), placeholder = area.querySelector(".scan-placeholder"), guide = area.querySelector(".scan-guide"), stopButton = $("#scan-stop"), status = $("#scan-status"), error = $("#pair-error");
  let scanner;
  const cleanup = () => {
    scanner?.stop();
    if (!area.isConnected) return;
    placeholder.hidden = false; video.hidden = true; guide.hidden = true; stopButton.hidden = true;
  };
  sheetCleanup = cleanup;
  placeholder.hidden = true; video.hidden = false; guide.hidden = false; stopButton.hidden = false;
  error.textContent = "";
  stopButton.onclick = () => { cleanup(); status.textContent = "Camera đã dừng. Mở camera hoặc chọn ảnh QR để tiếp tục."; };
  try {
    scanner = new CameraScanner(video, {
      onCode: value => area.isConnected && sheet.open ? pairValue(value) : false,
      onStatus: message => { if (area.isConnected) status.textContent = message; },
      onError: message => { if (area.isConnected) { cleanup(); error.textContent = message; status.textContent = "Camera đã dừng."; } },
    });
    await scanner.start();
  } catch (e) {
    cleanup();
    if (area.isConnected) { error.textContent = e.message; status.textContent = "Chưa bắt đầu quét."; }
  }
}
function deviceSheet() {
  showSheet(
    "Thiết bị của bạn",
    state.pairing
      ? `<p>Đang ghép đôi với <strong>${esc(state.name || state.pairing.name)}</strong>.</p><p>${esc(connection === "online" ? "PC đang kết nối." : connectText || "PC cần mở Lantern, mở khóa và không sleep.")}</p><button id="reconnect" class="primary">Kết nối lại</button><button id="pair-other" class="secondary">Ghép đôi lại</button><p>Bản web dùng tài khoản của PC. Có thể thu hồi thiết bị trong phần Điện thoại trên PC.</p>`
      : '<p>Chưa ghép đôi PC.</p><button id="pair-other" class="primary">Ghép đôi bằng QR</button>',
  );
  if ($("#reconnect"))
    $("#reconnect").onclick = () => {
      connect();
      closeSheet();
    };
  $("#pair-other").onclick = pairSheet;
}
async function unlockSheet(id) {
  if (current !== id) return;
  showSheet(
    "Mở khóa hội thoại",
    `<p>${esc(contact(id)?.name || "Hội thoại")} được bảo vệ bằng mật khẩu.</p><form id="unlock-form"><input id="conversation-password" type="password" autocomplete="current-password" aria-label="Mật khẩu hội thoại" placeholder="Mật khẩu hội thoại" required><p id="unlock-error" class="error"></p><button class="primary">Mở khóa</button></form>`,
  );
  $("#conversation-password").focus();
  $("#unlock-form").onsubmit = async (e) => {
    e.preventDefault();
    const password = $("#conversation-password").value,
      button = e.target.querySelector("button");
    button.disabled = true;
    button.textContent = "Đang mở khóa…";
    try {
      const old = state.boxes[id],
        salt = old ? un64(old.salt) : random(16),
        key = await passwordKey(password, salt);
      let cached = null;
      if (old?.box) {
        try {
          cached = await unseal(key, old.box, "Lantern-conversation:" + id);
        } catch {}
      }
      if (old?.pendingBox) {
        try {
          cached = {
            messages: cached?.messages || [],
            ...(await unseal(key, old.pendingBox, "Lantern-pending:" + id)),
          };
        } catch {}
      }
      let response;
      if (client?.ready)
        response = await client.request({
          type: "unlock",
          conversation: id,
          password,
        });
      else if (!cached) throw Error("Cần kết nối PC để mở khóa lần đầu.");
      sessionKeys.set(id, key);
      sessionPasswords.set(id, password);
      state.boxes[id] = { ...old, salt: b64(salt) };
      if (cached) {
        state.histories[id] = cached.messages;
        state.drafts[id] = cached.draft || "";
        $("#draft").value = state.drafts[id];
        autosize();
        for (const m of cached.outbox || [])
          if (!state.outbox.some((x) => x.id === m.id)) state.outbox.push(m);
      }
      if (response) update(response);
      closeSheet();
      lastRender = "";
      renderMessages(true);
      renderList();
      await save();
      sync();
      flushOutbox();
    } catch (e) {
      if ($("#unlock-error"))
        $("#unlock-error").textContent = e.message || "Mật khẩu chưa đúng.";
      button.disabled = false;
      button.textContent = "Mở khóa";
    }
  };
}
function lockLocal(id) {
  if (current === id) selectMessages(false);
  sessionKeys.delete(id);
  sessionPasswords.delete(id);
  delete state.histories[id];
  state.outbox = state.outbox.filter((m) => m.conversationId !== id);
  delete state.drafts[id];
  clearMedia(id);
  if (current === id) {
    replyTo = null;
    closeSheet();
    lastRender = "";
    renderMessages();
  }
}
async function lockConversation() {
  const id = current;
  await save();
  lockLocal(id);
  client?.request({ type: "lock", conversation: id }).catch(() => {});
  renderList();
}
async function lockApp() {
  if (!state) return;
  await save();
  client?.stop();
  connection = "offline";
  closeSheet();
  sessionKeys.clear();
  sessionPasswords.clear();
  clearMedia();
  state = null;
  current = "";
  vault.lock();
  await gate();
}
function settingsSheet() {
  showSheet(
    "Cài đặt",
    `<div class="setting"><div><strong>${esc(state.name || "Lantern trên điện thoại")}</strong><small>Bản web · 1.2.12</small></div><img src="/icon.svg" width="45" height="45" alt=""></div><label class="setting"><span>Đổi ký hiệu thành emoji<small>Ví dụ :) thành 🙂 khi gửi</small></span><input id="setting-emoji" type="checkbox" ${state.settings.emoticons ? "checked" : ""}></label><label class="setting"><span>Báo tin trong ứng dụng<small>Chỉ khi Lantern đang mở</small></span><input id="setting-notice" type="checkbox" ${state.settings.inAppNotice ? "checked" : ""}></label><div><p>Bảng màu</p><div class="theme-grid">${colorThemes.map((t) => `<button data-theme="${t.id}" aria-pressed="${(state.settings.colorTheme || "jade") === t.id}"><span style="background:${t.accent}"></span>${t.name}</button>`).join("")}</div></div><button id="settings-calculator" class="secondary">Mở máy tính</button><button id="settings-expression" class="secondary">Đổi phép tính mở khóa</button><button id="settings-lock" class="secondary">Khóa ứng dụng</button><button id="settings-update" class="text-action">${window.lanternUpdate?.waiting ? "Cài bản web mới" : "Kiểm tra cập nhật"}</button><button id="settings-reset" class="text-action">Tạo lại bản lưu trên điện thoại</button><p>Lịch sử mở từ bản đã lưu và đồng bộ khi PC kết nối. Khi iPhone dừng app, các tin mới sẽ được nhận ở lần mở tiếp theo.</p><p class="app-version">Lantern · Riêng tư, gần gũi, luôn kết nối.</p>`,
  );
  for (const b of sheet.querySelectorAll("[data-theme]"))
    b.onclick = () => {
      state.settings.colorTheme = b.dataset.theme;
      applyColorTheme();
      for (const x of sheet.querySelectorAll("[data-theme]"))
        x.setAttribute("aria-pressed", String(x === b));
      saveSoon();
    };
  $("#setting-emoji").onchange = (e) => {
    state.settings.emoticons = e.target.checked;
    saveSoon();
  };
  $("#setting-notice").onchange = (e) => {
    state.settings.inAppNotice = e.target.checked;
    saveSoon();
  };
  $("#settings-lock").onclick = () =>
    lockApp().catch(() => toast("Chưa lưu được dữ liệu."));
  $("#settings-calculator").onclick = showCalculator;
  $("#settings-expression").onclick = calculatorExpressionSheet;
  $("#settings-reset").onclick = resetSheet;
  $("#settings-update").onclick = async () => {
    await save();
    const r =
      window.lanternUpdate ||
      (await navigator.serviceWorker?.getRegistration());
    if (r?.waiting) {
      r.waiting.postMessage("activate");
    } else {
      await r?.update();
      toast("Đã kiểm tra bản web mới.");
    }
  };
}
function calculatorExpressionFields(prefix) {
  return `<fieldset class="expression-fields"><legend>Phép tính mở khóa riêng</legend><p>Nhập phép tính này trên máy tính rồi bấm = để mở Lantern. Dùng ít nhất 6 chữ số và một dấu + − × ÷. Chỉ áp dụng trên thiết bị này.</p><label>Phép tính mới<input id="${prefix}-expression" type="password" autocomplete="new-password" autocapitalize="off" spellcheck="false" required maxlength="160" placeholder="Phép tính của riêng bạn"></label><label>Nhập lại phép tính<input id="${prefix}-expression-confirm" type="password" autocomplete="new-password" autocapitalize="off" spellcheck="false" required maxlength="160" placeholder="Nhập lại để xác nhận"></label></fieldset>`;
}
function newCalculatorExpression(prefix) {
  const expression = validateCalculatorExpression($(`#${prefix}-expression`).value);
  if (expression !== validateCalculatorExpression($(`#${prefix}-expression-confirm`).value))
    throw Error("Hai phép tính chưa khớp.");
  return expression;
}
function calculatorExpressionSheet() {
  showSheet("Đổi phép tính mở khóa", `<form id="expression-form"><label>Phép tính hiện tại<input id="expression-current" type="password" autocomplete="current-password" required maxlength="160"></label>${calculatorExpressionFields("change")}<div class="error" role="alert"></div><button class="primary wide">Lưu phép tính</button></form>`);
  const form = $("#expression-form"), button = form.querySelector("button"), error = form.querySelector(".error");
  $("#expression-current").focus();
  form.onsubmit = async event => {
    event.preventDefault();
    if (button.disabled) return;
    button.disabled = true; button.textContent = "Đang lưu…"; error.textContent = "";
    try {
      const value = newCalculatorExpression("change");
      if (!await matchesCalculatorLock($("#expression-current").value)) throw Error("Phép tính hiện tại chưa đúng.");
      const record = await createCalculatorLock(value);
      if (isCalculator() || !sheet.open || sheet.querySelector("form") !== form) return;
      saveCalculatorLock(record);
      calculatorSetupComplete();
      closeSheet();
      toast("Đã đổi phép tính mở khóa trên thiết bị này.");
    } catch (e) { error.textContent = e.message || "Chưa lưu được phép tính. Hãy thử lại."; }
    finally { button.disabled = false; button.textContent = "Lưu phép tính"; }
  };
}
function messageSheet(m) {
  if (!m || isLocked(current)) return;
  showSheet(
    "Tin nhắn",
    `<div class="palette">${["👍", "❤️", "😂", "😮", "😢", "😡"].map((x) => `<button data-emoji="${x}" aria-label="Thả cảm xúc ${x}">${x}</button>`).join("")}</div><button class="text-action" id="quote-message">↩ Trả lời tin nhắn</button><button class="text-action" id="remove-reaction">Gỡ cảm xúc của mình</button><button class="text-action" id="select-message">Chọn nhiều tin nhắn</button><button class="text-action danger-text" id="delete-message">Xóa tin nhắn</button>${m.kind === "text" ? '<button class="text-action" id="copy-message">Sao chép nội dung</button>' : ""}`,
  );
  for (const b of sheet.querySelectorAll("[data-emoji]"))
    b.onclick = () => react(m, b.dataset.emoji);
  $("#remove-reaction").onclick = () => react(m, "");
  $("#select-message").onclick = () => {
    closeSheet();
    selectMessages(true, m.id);
  };
  $("#delete-message").onclick = () => confirmDelete([m.id]);
  $("#quote-message").onclick = () => {
    replyTo = m;
    $("#reply-banner").innerHTML =
      `<div><strong>${esc(m.senderName || contact(current)?.name)}</strong><br>${esc(m.text || m.fileName)}</div><button class="icon" aria-label="Hủy trả lời">${icon("close")}</button>`;
    $("#reply-banner").hidden = false;
    $("#reply-banner button").onclick = () => {
      replyTo = null;
      $("#reply-banner").hidden = true;
    };
    closeSheet();
    $("#draft").focus();
  };
  if ($("#copy-message"))
    $("#copy-message").onclick = async () => {
      try {
        await navigator.clipboard.writeText(m.text);
        closeSheet();
        toast("Đã sao chép");
      } catch {
        toast("Chạm giữ nội dung để sao chép.");
      }
    };
}
async function react(message, emoji) {
  if (!client?.ready) {
    toast("Kết nối PC để thả cảm xúc.");
    return;
  }
  closeSheet();
  try {
    await client.request({
      type: "react",
      conversation: current,
      messageId: message.id,
      emoji,
    });
    sync();
  } catch (e) {
    toast(e.message);
  }
}
function attachmentSheet() {
  showSheet(
    "Gửi nội dung",
    `<div class="palette">${["🙂", "❤️", "😄", "👍", "🎉", "🙏"].map((x) => `<button data-emoji="${x}">${x}</button>`).join("")}</div><button id="choose-image" class="text-action">🖼️ Ảnh hoặc video</button><button id="choose-file" class="text-action">📄 Tệp từ điện thoại</button><p>Tối đa 100 MB mỗi tệp. Giữ Lantern mở trong lúc gửi.</p>`,
  );
  for (const b of sheet.querySelectorAll("[data-emoji]"))
    b.onclick = () => {
      const input = $("#draft");
      input.value += b.dataset.emoji;
      closeSheet();
      input.focus();
      autosize();
    };
  $("#choose-image").onclick = () => {
    closeSheet();
    $("#file").accept = "image/*,video/*";
    $("#file").click();
  };
  $("#choose-file").onclick = () => {
    closeSheet();
    $("#file").accept = "";
    $("#file").click();
  };
}
function clearMedia(conversation) {
  for (const [id, item] of media)
    if (!conversation || item.conversation === conversation) {
      URL.revokeObjectURL(item.url);
      media.delete(id);
    }
}
async function download(m, automatic = false) {
  if (automatic) autoMediaAttempted.set(m.id, m.status);
  if (media.has(m.id)) return media.get(m.id);
  if (mediaJobs.has(m.id)) return null;
  if (!client?.ready) throw Error("Cần kết nối PC để tải tệp.");
  if (m.fileSize > 100 * 1024 * 1024) throw Error("Tệp vượt giới hạn 100 MB.");
  const id = m.conversationId || current,
    owner = state,
    activeClient = client;
  if (isLocked(id)) throw Error("Hội thoại đang khóa");
  mediaJobs.add(m.id);
  try {
    const parts = [];
    let offset = 0;
    do {
      if (isLocked(id) || state !== owner) throw Error("Hội thoại đã khóa");
      while (sending) await new Promise((r) => setTimeout(r, 30));
      const r = await activeClient.request({
        type: "download",
        conversation: id,
        messageId: m.id,
        offset,
      });
      const bytes = un64(r.data);
      if (!bytes.length && offset < m.fileSize) throw Error("Tệp chưa tải đủ");
      parts.push(bytes);
      offset += bytes.length;
      if (offset > m.fileSize) throw Error("Tệp không hợp lệ");
    } while (offset < m.fileSize);
    const bytes = concat(...parts);
    if (m.hash && hex(await sha(bytes)) !== m.hash.toUpperCase())
      throw Error("Tệp tải về sai checksum");
    if (isLocked(id) || state !== owner) throw Error("Hội thoại đã khóa");
    let type =
      m.kind === "image"
        ? m.fileName?.toLowerCase().endsWith(".png")
          ? "image/png"
          : "image/jpeg"
        : m.kind === "video"
          ? "video/mp4"
          : "application/octet-stream";
    const blob = new Blob([bytes], { type });
    const item = { url: URL.createObjectURL(blob), blob, conversation: id };
    for (const [oldId, old] of media) {
      if (
        [...media.values()].reduce((n, x) => n + x.blob.size, 0) + blob.size <=
        32 * 1024 * 1024
      )
        break;
      URL.revokeObjectURL(old.url);
      media.delete(oldId);
    }
    media.set(m.id, item);
    return item;
  } finally {
    mediaJobs.delete(m.id);
  }
}
async function openMedia(m) {
  try {
    toast("Đang mở tệp…");
    const item = await download(m);
    if (!item) return;
    showSheet(
      m.fileName || "Nội dung chia sẻ",
      `${m.kind === "image" ? `<img src="${item.url}" class="media-preview" alt="${esc(m.fileName)}">` : m.kind === "video" ? `<video src="${item.url}" controls playsinline class="media-preview"></video>` : ""}<a class="secondary wide" href="${item.url}" download="${esc(m.fileName)}">Lưu tệp</a><button id="share-file" class="primary">Chia sẻ / Lưu vào Tệp</button>`,
    );
    $("#share-file").onclick = async () => {
      const file = new File([item.blob], m.fileName || "Lantern", {
        type: item.blob.type,
      });
      try {
        if (navigator.canShare?.({ files: [file] }))
          await navigator.share({ files: [file] });
        else sheet.querySelector("a").click();
      } catch (e) {
        if (e.name !== "AbortError") toast("Dùng nút Lưu tệp để tải xuống.");
      }
    };
  } catch (e) {
    toast(e.message);
  }
}
async function acceptFile(m) {
  if (!client?.ready) {
    toast("Kết nối PC để nhận tệp.");
    return;
  }
  try {
    await client.request(
      { type: "accept", conversation: current, messageId: m.id },
      60000,
    );
    toast("Đang nhận tệp về PC…");
    sync();
  } catch (e) {
    toast(e.message);
  }
}
async function upload(file) {
  if (!file || fileBusy) return;
  if (!client?.ready) {
    toast("Kết nối PC trước khi gửi ảnh hoặc tệp.");
    return;
  }
  if (file.size > 100 * 1024 * 1024) {
    toast("Tệp vượt giới hạn 100 MB.");
    return;
  }
  const conversation = current,
    owner = state,
    activeClient = client;
  if (isLocked(conversation)) return;
  fileBusy = true;
  const progress = $("#transfer");
  progress.hidden = false;
  progress.textContent = "Đang chuẩn bị " + file.name + "…";
  try {
    const bytes = new Uint8Array(await file.arrayBuffer()),
      hash = hex(await sha(bytes)),
      id = uid(),
      name = file.name || "Anh-dan.png";
    if (state !== owner || isLocked(conversation))
      throw Error("Hội thoại đã khóa");
    await activeClient.request({
      type: "upload-start",
      conversation,
      id,
      fileName: name,
      size: bytes.length,
      hash,
    });
    for (let offset = 0; offset < bytes.length; offset += 32768) {
      if (state !== owner || isLocked(conversation))
        throw Error("Hội thoại đã khóa");
      while (sending) await new Promise((r) => setTimeout(r, 30));
      await activeClient.request({
        type: "upload-chunk",
        conversation,
        offset,
        data: b64(bytes.subarray(offset, offset + 32768)),
      });
      progress.textContent = `Đang gửi ${name} · ${Math.min(100, Math.round(((offset + 32768) / bytes.length) * 100))}%`;
    }
    if (state !== owner || isLocked(conversation))
      throw Error("Hội thoại đã khóa");
    await activeClient.request(
      { type: "upload-finish", conversation, fileName: name },
      60000,
    );
    toast("Đã gửi tệp tới PC");
    await sync();
  } catch (e) {
    toast(e.message + " Chọn lại tệp để gửi tiếp.");
  } finally {
    fileBusy = false;
    progress.hidden = true;
  }
}
function infoSheet() {
  if (!current || isLocked(current)) return;
  const id = current;
  showSheet(
    "Thông tin hội thoại",
    `<div class="tabs"><button data-tab="settings" class="active">Cài đặt</button><button data-tab="search">Tìm kiếm</button><button data-tab="shared">Đã chia sẻ</button></div><div id="info-content"></div>`,
  );
  function tab(type) {
    for (const b of sheet.querySelectorAll("[data-tab]"))
      b.classList.toggle("active", b.dataset.tab === type);
    const area = $("#info-content");
    area.classList.toggle("conversation-settings-actions", type === "settings");
    if (type === "settings") {
      area.innerHTML = `<p><strong>${esc(contact(id)?.name)}</strong></p>${contact(id)?.isGroup ? "" : '<button class="secondary wide" id="rename-conversation">Đặt tên riêng</button>'}<button class="secondary wide" id="timer-conversation">Tin nhắn tự hủy · ${esc(disappearDuration(contact(id)?.disappearSeconds || 0))}</button>${contact(id)?.protected ? '<button class="secondary wide" id="lock-conversation">Khóa hội thoại ngay</button>' : "<p>Đặt mật khẩu hội thoại trong Lantern trên PC.</p>"}`;
      if ($("#rename-conversation")) $("#rename-conversation").onclick = () => editConversation("name");
      $("#timer-conversation").onclick = () => editConversation("timer");
      area.insertAdjacentHTML(
        "beforeend",
        '<button class="text-action wide" id="select-many">Chọn nhiều tin nhắn</button><button class="text-action wide danger-text" id="clear-history">Dọn lịch sử hội thoại</button>',
      );
      $("#select-many").onclick = () => {
        closeSheet();
        selectMessages(true);
      };
      $("#clear-history").onclick = () => confirmDelete(null);
      if ($("#lock-conversation"))
        $("#lock-conversation").onclick = () =>
          lockConversation().catch((e) => toast(e.message));
    } else if (type === "search") {
      area.innerHTML =
        '<input id="find-message" class="search" placeholder="Tìm trong tin đã lưu"><div id="find-results"></div>';
      $("#find-message").oninput = () => {
        $("#find-results").innerHTML = history(id)
          .filter((m) =>
            m.text
              ?.toLowerCase()
              .includes($("#find-message").value.toLowerCase()),
          )
          .map(
            (m) =>
              `<p class="result">${esc(m.text)}<br><small>${clock(m.time)}</small></p>`,
          )
          .join("");
      };
      $("#find-message").focus();
    } else {
      area.innerHTML =
        '<div class="tabs"><button data-kind="media" class="active">Ảnh/video</button><button data-kind="file">Tệp</button><button data-kind="links">Liên kết</button></div><div id="shared-results"></div>';
      const shared = (kind) => {
        for (const b of area.querySelectorAll("[data-kind]"))
          b.classList.toggle("active", b.dataset.kind === kind);
        const list = history(id),
          results = $("#shared-results");
        if (kind === "links") {
          const urls = [
            ...new Set(
              list.flatMap((m) => m.text?.match(/https?:\/\/[^\s<>]+/g) || []),
            ),
          ];
          results.className = "";
          results.innerHTML =
            urls
              .map(
                (url) =>
                  `<p class="result"><a href="${esc(url)}" target="_blank" rel="noopener noreferrer">${esc(url)}</a></p>`,
              )
              .join("") || "<p>Chưa có liên kết.</p>";
        } else {
          const selected = list.filter((m) =>
            kind === "media"
              ? ["image", "video"].includes(m.kind)
              : m.kind === "file",
          );
          results.className = kind === "media" ? "media-grid" : "";
          results.innerHTML =
            selected
              .map(
                (m) =>
                  `<button data-id="${m.id}" class="${kind === "file" ? "text-action wide" : ""}">${media.has(m.id) && m.kind === "image" ? `<img src="${media.get(m.id).url}" alt="${esc(m.fileName)}">` : esc((m.kind === "video" ? "🎬 " : m.kind === "image" ? "🖼️ " : "📄 ") + m.fileName)}</button>`,
              )
              .join("") || "<p>Chưa có nội dung.</p>";
          for (const b of results.querySelectorAll("button"))
            b.onclick = () =>
              openMedia(selected.find((m) => m.id === b.dataset.id));
        }
      };
      for (const b of area.querySelectorAll("[data-kind]"))
        b.onclick = () => shared(b.dataset.kind);
      shared("media");
    }
  }
  for (const b of sheet.querySelectorAll("[data-tab]"))
    b.onclick = () => tab(b.dataset.tab);
  tab("settings");
}
function disappearDuration(seconds) {
  return ({0:"Tắt",10:"10 giây",30:"30 giây",60:"1 phút",300:"5 phút",3600:"1 giờ",86400:"1 ngày",604800:"7 ngày"})[seconds] || seconds + " giây";
}
function editConversation(kind) {
  const id = current, c = contact(id), owner = state, activeClient = client;
  if (!c || isLocked(id)) return;
  if (!activeClient?.ready) { toast("Kết nối PC để lưu tùy chỉnh hội thoại."); return; }
  if (!activeClient.capabilities.includes("conversation-settings-v1")) { toast("Cập nhật Lantern trên PC để dùng tùy chỉnh hội thoại."); return; }
  const seconds = c.disappearSeconds || 0, times = [...new Set([0,10,30,60,300,3600,86400,604800,seconds])];
  showSheet(kind === "name" ? "Đặt tên riêng" : "Tin nhắn tự hủy", `<form id="conversation-settings-form">${kind === "name" ? `<p>Chỉ bạn thấy tên này trên PC và điện thoại. Để trống để dùng tên gốc: ${esc(c.originalName || c.name)}.</p><label>Tên riêng<input id="conversation-setting-value" value="${esc(c.alias || "")}" maxlength="80" autocomplete="off"></label>` : `<p>Áp dụng cho tin mới ở cả hai chiều. Đếm ngược từ khi người nhận đọc; trong nhóm là lượt đọc đầu tiên. Tin và tệp do Lantern lưu sẽ được xóa khi hết giờ.</p><label>Thời gian tự hủy<select id="conversation-setting-value">${times.map(t=>`<option value="${t}" ${t === seconds ? "selected" : ""}>${disappearDuration(t)}</option>`).join("")}</select></label>`}<div class="error" role="alert"></div><button class="primary wide">Lưu thay đổi</button></form>`);
  const form = $("#conversation-settings-form"), field = $("#conversation-setting-value"), button = form.querySelector("button"), error = form.querySelector(".error");
  if (kind === "name") field.focus();
  form.onsubmit = async event => {
    event.preventDefault();
    if (button.disabled || state !== owner || current !== id || isLocked(id) || isCalculator()) return;
    button.disabled = true; button.textContent = "Đang lưu trên PC…"; error.textContent = "";
    try {
      if (client !== activeClient || !activeClient.ready) throw Error("Kết nối PC đã thay đổi. Hãy thử lại.");
      const packet = await activeClient.request({type:kind === "name" ? "conversation-rename" : "conversation-timer", conversation:id, ...(kind === "name" ? {name:field.value.trim()} : {disappearSeconds:Number(field.value)})});
      if (state !== owner) return;
      update(packet); await save();
      if (current === id && !isCalculator() && !isLocked(id) && $("#conversation-settings-form") === form) { closeSheet(); toast("Đã lưu tùy chỉnh hội thoại."); infoSheet(); }
    } catch (e) { error.textContent = e.message; button.disabled = false; button.textContent = "Lưu thay đổi"; }
  };
}
function resizeViewport() {
  if (!$("#app .shell")) return;
  const v = visualViewport;
  document.documentElement.style.setProperty(
    "--height",
    (v?.height || innerHeight) + "px",
  );
  document.documentElement.style.setProperty(
    "--top",
    (v?.offsetTop || 0) + "px",
  );
}
window.visualViewport?.addEventListener("resize", resizeViewport);
window.visualViewport?.addEventListener("scroll", resizeViewport);
document.addEventListener("visibilitychange", () => {
  if (document.hidden) {
    hiddenAt = Date.now();
    save().catch(() => {});
  } else if (state) {
    for (const id of Object.keys(state.histories))
      state.histories[id] = history(id);
    lastRender = "";
    renderMessages();
    if (Date.now() - hiddenAt > 3000 || !client?.ready) connect();
    else {
      sync();
      flushOutbox();
    }
  }
});
document.addEventListener("calculator-mode", (event) => {
  if (event.detail.covered) { clearTimeout(markRead.timer); closeSheet(); }
  else { resizeViewport(); if (state) { lastRender = ""; renderMessages(); sync(); } else $("#gate-password")?.focus(); }
});
window.addEventListener("online", () => {
  if (state?.pairing && !client?.ready) connect();
});
window.addEventListener("pagehide", () => client?.stop());
window.addEventListener("pageshow", (e) => {
  if (e.persisted && state?.pairing) connect();
});
setInterval(() => {
  if (state && !document.hidden) {
    const expired = Object.values(state.histories).some((items) =>
      items.some((m) => !alive(m)),
    );
    if (expired) {
      for (const id of Object.keys(state.histories))
        state.histories[id] = history(id);
      lastRender = "";
      renderMessages();
      saveSoon();
    }
    if (client?.ready) {
      sync();
      flushOutbox();
    }
  }
}, 4000);
const $ = (s) => document.querySelector(s),
  app = $("#app"),
  sheet = $("#sheet");
const esc = (x) =>
  String(x ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const shapes = {
  back: "m14 6-6 6 6 6",
  send: "m5 12 14-8-5 16-3-7-6-1m6 1 8-9",
  plus: "M12 5v14M5 12h14",
  more: "M5 12h.01M12 12h.01M19 12h.01",
  close: "m6 6 12 12M18 6 6 18",
  chat: "M4 4h16v12H9l-5 4V4m4 5h8m-8 3h5",
  lock: "M7 11V7a5 5 0 0 1 10 0v4M5 11h14v10H5zM12 15v3",
  settings:
    "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8M12 2v3m0 14v3M2 12h3m14 0h3M5 5l2 2m10 10 2 2M5 19l2-2M17 7l2-2",
  scan: "M8 3H3v5m13-5h5v5M3 16v5h5m8 0h5v-5M7 7h3v3H7zm7 0h3v3h-3zM7 14h3v3H7zm7 0h3v3h-3z",
  phone: "M7 2h10v20H7zM11 18h2",
  check: "m5 12 4 4L19 6",
  search: "M15 15l6 6M10 3a7 7 0 1 0 0 14 7 7 0 0 0 0-14",
};
const icon = (name) =>
  `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${shapes[name] || shapes.chat}"/></svg>`;
const initials = (name) =>
  String(name || "?")
    .trim()
    .split(/\s+/)
    .slice(-2)
    .map((w) => w[0])
    .join("")
    .toUpperCase();
const clock = (time) =>
  new Date(time).toLocaleTimeString("vi-VN", {
    hour: "2-digit",
    minute: "2-digit",
  });
const sizeText = (n) =>
  n < 1024
    ? n + " B"
    : n < 1048576
      ? (n / 1024).toFixed(0) + " KB"
      : (n / 1048576).toFixed(1) + " MB";
const presence = (p) =>
  ({
    online: "Đang hoạt động",
    idle: "Tạm vắng",
    dnd: "Không làm phiền",
    offline: "Ngoại tuyến",
    group: "Hội thoại nhóm",
  })[p] || "Ngoại tuyến";
let vault,
  state,
  client,
  current = "",
  replyTo = null,
  connection = "offline",
  connectText = "",
  persistTail = Promise.resolve(),
  sheetCleanup = null,
  saveTimer,
  sending = false,
  syncBusy = false,
  lastRender = "",
  hiddenAt = 0,
  fileBusy = false,
  restoring = false,
  lastServerSerial = 0;
let selecting = false;
const selectedMessages = new Set();
const sessionKeys = new Map(),
  sessionPasswords = new Map(),
  media = new Map(),
  mediaJobs = new Set(),
  autoMediaAttempted = new Map();
const history = (id) => (state?.histories?.[id] || []).filter((x) => alive(x));
const contact = (id) => state?.conversations?.find((c) => c.id === id);
const isLocked = (id) => !!contact(id)?.protected && !sessionKeys.has(id);
function closeSheet() {
  sheetCleanup?.();
  sheetCleanup = null;
  if (sheet.open) sheet.close();
  sheet.replaceChildren();
}
sheet.addEventListener("cancel", () => {
  sheetCleanup?.();
  sheetCleanup = null;
});
function showSheet(title, body) {
  closeSheet();
  sheet.innerHTML = `<div class="grab"></div><div class="sheet-head"><h2 id="sheet-title">${esc(title)}</h2><button class="icon" aria-label="Đóng">${icon("close")}</button></div><div class="sheet-body">${body}</div>`;
  sheet.querySelector(".sheet-head button").onclick = closeSheet;
  if (!isCalculator()) sheet.showModal();
  return sheet;
}
function save() {
  if (!state || !vault.key) return Promise.resolve();
  const owner = state,
    data = structuredClone(state),
    keys = new Map(sessionKeys);
  const task = persistTail.then(async () => {
    for (const c of data.conversations) {
      if (!c.protected) continue;
      const key = keys.get(c.id);
      if (key) {
        const salt = data.boxes[c.id]?.salt || b64(random(16));
        data.boxes[c.id] = {
          salt,
          box: await seal(
            key,
            {
              messages: data.histories[c.id] || [],
            },
            "Lantern-conversation:" + c.id,
          ),
          pendingBox: await seal(
            key,
            {
              draft: data.drafts[c.id] || "",
              outbox: data.outbox.filter((m) => m.conversationId === c.id),
            },
            "Lantern-pending:" + c.id,
          ),
        };
        if (
          state === owner &&
          (contact(c.id)?.historyVersion || 0) === (c.historyVersion || 0)
        )
          state.boxes[c.id] = data.boxes[c.id];
      } else if (owner.boxes[c.id]) data.boxes[c.id] = owner.boxes[c.id];
      delete data.histories[c.id];
      delete data.drafts[c.id];
    }
    data.outbox = data.outbox.filter(
      (m) =>
        !data.conversations.find((c) => c.id === m.conversationId)?.protected,
    );
    await vault.save(data);
  });
  persistTail = task.catch(() => {});
  return task;
}
function saveSoon() {
  clearTimeout(saveTimer);
  saveTimer = setTimeout(
    () =>
      save().catch(() =>
        toast("Không lưu được dữ liệu. Kiểm tra dung lượng điện thoại."),
      ),
    140,
  );
}
function newState(identity) {
  return {
    identity,
    pairing: null,
    conversations: [],
    histories: {},
    boxes: {},
    outbox: [],
    drafts: {},
    settings: { emoticons: true, inAppNotice: true },
    name: "",
  };
}
async function gate() {
  const existing = await vault.get("header");
  const setupCalculator = !hasCalculatorLock();
  app.innerHTML = `<main class="gate"><div class="brand"><img src="/icon.svg" alt="">Lantern</div><div class="eyebrow">Không gian của bạn</div><h1>${existing ? "Chào mừng<br>trở lại." : "Mang cuộc trò chuyện<br>theo bạn."}</h1><p class="intro">${existing ? "Mở khóa để tiếp tục những cuộc trò chuyện." : "Kết nối với Lantern trên PC. Một tài khoản, những cuộc trò chuyện luôn ở gần."}</p><form id="gate-form"><label>${existing ? "Mật khẩu ứng dụng" : "Tạo mật khẩu trên điện thoại"}<input id="gate-password" type="password" autocomplete="${existing ? "current-password" : "new-password"}" required minlength="${existing ? 1 : 8}" placeholder="${existing ? "Nhập mật khẩu" : "Ít nhất 8 ký tự"}"></label>${existing ? "" : '<label>Nhập lại mật khẩu<input id="gate-confirm" type="password" autocomplete="new-password" required minlength="8" placeholder="Nhập lại mật khẩu"></label>'}${setupCalculator ? calculatorExpressionFields("gate") : ""}<div class="error" role="alert"></div><button class="primary wide">${existing ? "Mở khóa" : "Bắt đầu"}</button></form>${existing ? '<button class="link-button" id="forget">Quên mật khẩu?</button>' : ""}<p class="footnote">Lịch sử trên điện thoại được mã hóa.<br>PC giữ tài khoản và bản lưu chính.</p></main>`;
  $("#gate-password").focus();
  $("#gate-form").onsubmit = async (e) => {
    e.preventDefault();
    const btn = e.target.querySelector("button"),
      error = e.target.querySelector(".error"),
      password = $("#gate-password").value;
    error.textContent = "";
    if (btn.disabled) return;
    let expression;
    if (setupCalculator) {
      try { expression = newCalculatorExpression("gate"); }
      catch (e) { error.textContent = e.message; return; }
    }
    if (!existing && password !== $("#gate-confirm").value) {
      error.textContent = "Hai mật khẩu chưa khớp.";
      return;
    }
    btn.disabled = true;
    btn.textContent = "Đang mở…";
    try {
      if (existing) state = await vault.unlock(password);
      else {
        state = newState(await createIdentity());
        await vault.create(password, state);
      }
      if (setupCalculator) saveCalculatorLock(await createCalculatorLock(expression));
      calculatorSetupComplete();
      state.drafts ||= {};
      state.boxes ||= {};
      shell();
      navigator.storage?.persist?.().catch(() => {});
      if (state.pairing) connect();
      else pairSheet();
    } catch {
      state = null;
      vault.lock();
      error.textContent = existing
        ? "Mật khẩu chưa đúng hoặc dữ liệu không đọc được."
        : "Không tạo được bộ nhớ. Hãy dùng Safari ngoài chế độ riêng tư.";
      btn.disabled = false;
      btn.textContent = existing ? "Mở khóa" : "Bắt đầu";
    }
  };
  if (existing) $("#forget").onclick = () => resetSheet();
}
function resetSheet() {
  const code = String(
    (crypto.getRandomValues(new Uint32Array(1))[0] % 90000) + 10000,
  );
  showSheet(
    "Tạo lại bản lưu trên điện thoại",
    `<p>Thao tác này xóa ghép đôi và dữ liệu lưu trên điện thoại. Lịch sử trên PC vẫn còn. Sau đó cần PC cho phép ghép đôi lại.</p><p>Để xác nhận, nhập <strong>XÓA ${code}</strong>.</p><form><input aria-label="Mã xác nhận xóa" placeholder="Nhập đúng cụm xác nhận" autocomplete="off"><button class="danger" disabled>Xóa bản lưu điện thoại</button></form>`,
  );
  const input = sheet.querySelector("input"),
    button = sheet.querySelector(".danger");
  input.oninput = () => (button.disabled = input.value !== `XÓA ${code}`);
  sheet.querySelector("form").onsubmit = async (e) => {
    e.preventDefault();
    if (input.value !== `XÓA ${code}`) return;
    button.disabled = true;
    client?.stop();
    await persistTail;
    await vault.reset();
    state = null;
    sessionKeys.clear();
    sessionPasswords.clear();
    clearMedia();
    closeSheet();
    await gate();
  };
}
function shell() {
  applyColorTheme();
  app.innerHTML = `<main class="shell"><section id="inbox"><header class="topbar"><div class="brand"><img src="/icon.svg" alt="">Lantern</div><button id="top-settings" class="icon" aria-label="Cài đặt">${icon("settings")}</button></header><div id="status" class="statusline"></div></section><section id="list" class="list-view"><div class="section-title"><h2>Tin nhắn</h2><span id="list-count"></span></div><input id="search" class="search" type="search" placeholder="Tìm cuộc trò chuyện" aria-label="Tìm cuộc trò chuyện"><div id="conversations"></div></section><section id="chat" class="chat-view" hidden><header class="chat-head"><button id="back" class="icon" aria-label="Quay lại">${icon("back")}</button><div id="chat-avatar" class="avatar"></div><div class="chat-title"><strong id="chat-name"></strong><div id="chat-status" class="chat-status"></div></div><button id="info" class="icon" aria-label="Thông tin hội thoại">${icon("more")}</button></header><div id="selection-bar" class="selection-bar" hidden><span id="selection-count"></span><button id="delete-selected">Xóa đã chọn</button><button id="cancel-selection">Hủy</button></div><div id="timeline" class="timeline" role="log" aria-label="Tin nhắn"></div><div id="transfer" class="transfer" hidden></div><div id="composer-area" class="composer-area"><div id="reply-banner" class="reply-banner" hidden></div><form id="composer" class="composer"><button id="attach" class="icon" type="button" aria-label="Đính kèm">${icon("plus")}</button><textarea id="draft" rows="1" placeholder="Tin nhắn…" aria-label="Nhập tin nhắn"></textarea><button id="send" class="icon send" type="submit" aria-label="Gửi tin nhắn" disabled>${icon("send")}</button></form><div id="draft-note" class="draft-note" hidden></div></div></section><nav id="nav" class="nav"><button class="active" id="nav-chat">${icon("chat")}Tin nhắn</button><button id="nav-pair">${icon("phone")}Thiết bị</button><button id="nav-settings">${icon("settings")}Cài đặt</button></nav><input id="file" type="file" hidden></main>`;
  $("#top-settings").onclick = $("#nav-settings").onclick = settingsSheet;
  const pcButton = document.createElement("button");
  pcButton.id = "nav-pc"; pcButton.innerHTML = `${icon("phone")}Máy tính`;
  $("#nav").insertBefore(pcButton, $("#nav-settings"));
  pcButton.onclick = () => openPcPanel({ getClient: () => client, available: () => !!state && !document.hidden && !isCalculator(), showSheet, cleanup: fn => { sheetCleanup = fn; }, esc });
  $("#nav-pair").onclick = deviceSheet;
  $("#nav-chat").onclick = back;
  $("#back").onclick = back;
  $("#search").oninput = renderList;
  $("#info").onclick = infoSheet;
  $("#attach").onclick = attachmentSheet;
  $("#delete-selected").onclick = () => confirmDelete([...selectedMessages]);
  $("#cancel-selection").onclick = () => selectMessages(false);
  $("#composer").onsubmit = sendText;
  $("#send").onpointerdown = (e) => e.preventDefault();
  $("#draft").oninput = () => {
    autosize();
    state.drafts[current] = $("#draft").value;
    saveSoon();
  };
  $("#draft").onpaste = (e) => {
    const image = [...e.clipboardData.items].find((i) =>
      i.type.startsWith("image/"),
    );
    if (image) {
      e.preventDefault();
      upload(image.getAsFile());
    }
  };
  $("#file").onchange = () => {
    const file = $("#file").files[0];
    $("#file").value = "";
    if (file) upload(file);
  };
  $("#timeline").addEventListener("click", (e) => {
    if (selecting) {
      const row = e.target.closest("[data-message]");
      if (row) {
        selectedMessages.has(row.dataset.message)
          ? selectedMessages.delete(row.dataset.message)
          : selectedMessages.add(row.dataset.message);
        renderSelection();
      }
      return;
    }
    const action = e.target.closest("[data-action]");
    if (action) {
      const m = messages().find((x) => x.id === action.dataset.id);
      if (!m) return;
      if (action.dataset.action === "retry") flushOutbox();
      else if (action.dataset.action === "media") openMedia(m);
      else if (action.dataset.action === "accept") acceptFile(m);
      else messageSheet(m);
      return;
    }
    const bubble = e.target.closest(".bubble");
    if (bubble && e.detail === 2)
      messageSheet(messages().find((x) => x.id === bubble.dataset.id));
  });
  let hold, holdAt;
  $("#timeline").onpointerdown = (e) => {
    holdAt = [e.clientX, e.clientY];
    const b = e.target.closest(".bubble");
    if (selecting || !b || e.target.closest("button,a")) return;
    hold = setTimeout(
      () => messageSheet(messages().find((x) => x.id === b.dataset.id)),
      420,
    );
  };
  $("#timeline").onpointermove = (e) => {
    if (holdAt && Math.hypot(e.clientX - holdAt[0], e.clientY - holdAt[1]) > 12)
      clearTimeout(hold);
  };
  for (const event of ["pointerup", "pointercancel", "scroll"])
    $("#timeline").addEventListener(event, () => clearTimeout(hold));
  $("#timeline").oncontextmenu = (e) => {
    const b = e.target.closest(".bubble");
    if (b) {
      e.preventDefault();
      messageSheet(messages().find((x) => x.id === b.dataset.id));
    }
  };
  $("#timeline").addEventListener("scroll", markRead, { passive: true });
  renderList();
  renderStatus();
  resizeViewport();
}
function autosize() {
  const field = $("#draft");
  if (!field) return;
  field.style.height = "auto";
  field.style.height = Math.min(field.scrollHeight, 130) + "px";
  $("#send").disabled = !field.value.trim() || isLocked(current);
}
function renderStatus() {
  const title =
    connection === "online"
      ? "Đã kết nối " + (state?.name || "PC")
      : connection === "connecting"
        ? "Đang kết nối PC…"
        : connection === "approving"
          ? state?.pairing?.nonce
            ? "Chờ PC cho phép ghép đôi…"
            : "Đang xác thực PC…"
          : "PC chưa kết nối · Tin nhắn được giữ trên máy";
  if ($("#status"))
    $("#status").innerHTML =
      `<span class="dot ${connection === "online" ? "online" : ""}"></span>${esc(title)}`;
  if (current && $("#chat-status"))
    $("#chat-status").innerHTML =
      `<span class="dot ${esc(contact(current)?.presence || "offline")}"></span>${esc(connection === "online" ? presence(contact(current)?.presence) : "Đang chờ PC · Xem bản đã lưu")}`;
  if ($("#draft-note")) {
    $("#draft-note").hidden = connection === "online";
    $("#draft-note").textContent =
      "Tin gửi được giữ trên máy, tự gửi khi PC kết nối lại.";
  }
}
function renderList() {
  if (!$("#conversations")) return;
  const query = $("#search").value.trim().toLocaleLowerCase("vi"),
    all = state.conversations.filter((c) =>
      c.name.toLocaleLowerCase("vi").includes(query),
    );
  $("#list-count").textContent = state.conversations.length
    ? state.conversations.length + " hội thoại"
    : "";
  const install =
    !navigator.standalone && !matchMedia("(display-mode: standalone)").matches
      ? '<div class="install-note">Trên iPhone: mở bằng Safari → Chia sẻ → <strong>Thêm vào Màn hình chính</strong> để dùng Lantern như một ứng dụng.</div>'
      : "";
  $("#conversations").innerHTML =
    install +
    (all.length
      ? all
          .map((c) => {
            const last = reconcile(history(c.id), state.outbox, c.id).at(-1);
            return `<button class="conversation" data-id="${esc(c.id)}"><span class="avatar">${esc(initials(c.name))}</span><span class="contact-copy"><span class="contact-name">${esc(c.name)}${c.protected ? ' <span aria-label="Có mật khẩu">⌑</span>' : ""}</span><span class="presence"><i class="dot ${esc(c.presence)}"></i>${esc(presence(c.presence))}</span><p>${esc(isLocked(c.id) ? "Hội thoại được bảo vệ" : last ? (last.kind === "text" ? last.text : last.fileName || "Nội dung chia sẻ") : "Bắt đầu trò chuyện")}</p></span><span class="contact-meta">${last ? esc(clock(last.time)) : ""}${c.unread ? `<span class="counter">${Math.min(c.unread, 99)}${c.unread > 99 ? "+" : ""}</span>` : ""}</span></button>`;
          })
          .join("")
      : `<div class="empty"><div class="illustration">${icon("chat")}</div><h2>${query ? "Chưa tìm thấy" : "Những cuộc trò chuyện<br>luôn ở gần"}</h2><p>${query ? "Thử một tên khác." : state.pairing ? "Mở khóa Lantern trên PC để đồng bộ danh sách hội thoại." : "Ghép đôi với PC bằng QR để bắt đầu."}</p>${state.pairing ? "" : '<button class="primary" id="pair-now">Ghép đôi PC</button>'}</div>`);
  for (const row of $("#conversations").querySelectorAll(".conversation"))
    row.onclick = () => openConversation(row.dataset.id);
  if ($("#pair-now")) $("#pair-now").onclick = pairSheet;
}
function back() {
  selectMessages(false);
  current = "";
  replyTo = null;
  $("#chat").hidden = true;
  $("#list").hidden = false;
  $("#inbox").hidden = false;
  $("#nav").hidden = false;
  closeSheet();
  renderList();
  sync();
}
function openConversation(id) {
  selectMessages(false);
  current = id;
  replyTo = null;
  lastRender = "";
  $("#list").hidden = true;
  $("#inbox").hidden = true;
  $("#nav").hidden = true;
  $("#chat").hidden = false;
  $("#chat-name").textContent = contact(id)?.name || "Hội thoại";
  $("#chat-avatar").textContent = initials(contact(id)?.name);
  $("#draft").value = state.drafts[id] || "";
  $("#reply-banner").hidden = true;
  renderMessages(true);
  renderStatus();
  autosize();
  if (isLocked(id)) unlockSheet(id);
  else sync();
}
function messages() {
  return current && !isLocked(current)
    ? reconcile(history(current), state.outbox, current)
    : [];
}
function renderMessages(bottom = false) {
  if (!current || !$("#timeline")) return;
  $("#chat-name").textContent = contact(current)?.name || "Hội thoại";
  $("#chat-avatar").textContent = initials(contact(current)?.name);
  const locked = isLocked(current);
  $("#composer-area").hidden = locked;
  $("#info").disabled = locked;
  if (locked) {
    $("#timeline").innerHTML =
      `<div class="empty"><div class="illustration">${icon("lock")}</div><h2>Hội thoại đang khóa</h2><p>Mở khóa để xem tin nhắn và nội dung chia sẻ.</p><button class="primary" id="unlock-now">Mở khóa hội thoại</button></div>`;
    $("#unlock-now").onclick = () => unlockSheet(current);
    return;
  }
  const items = messages(),
    key = JSON.stringify(items);
  if (key === lastRender && !bottom) return;
  lastRender = key;
  const box = $("#timeline"),
    nearBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 100,
    oldTop = box.scrollTop;
  box.innerHTML = items.length
    ? items
        .map((m, i) => {
          const next = items[i + 1],
            prev = items[i - 1],
            grouped = joins(prev, m),
            tail = !joins(m, next),
            quoted = m.replyToId
              ? items.find((x) => x.id === m.replyToId)
              : null;
          const status = m._pending
            ? m._failed
              ? "Chờ gửi lại"
              : "Đang gửi…"
            : {
                read: "Đã đọc",
                delivered: "Đã nhận",
                sent: "Đã gửi",
                queued: "Đã tới PC",
                received: "Đã nhận",
              }[m.status] || m.status;
          const reactions = Object.values(m.reactions || {}).filter(Boolean),
            counts = [...new Set(reactions)]
              .map((r) => r + " " + reactions.filter((x) => x === r).length)
              .join(" ");
          let content =
            m.kind === "text"
              ? `<div class="body">${esc(m.text)}</div>`
              : m.kind === "contact"
                ? `<div class="file-card"><span class="file-symbol">👤</span><div><strong>${esc(m.contact?.name || m.text)}</strong><small>Danh thiếp · mở trên PC để kết nối</small></div></div>`
                : `<div class="file-card"><span class="file-symbol">${m.kind === "image" ? "🖼️" : m.kind === "video" ? "🎬" : "📄"}</span><div><strong>${esc(m.fileName)}</strong><small>${sizeText(m.fileSize || 0)}</small></div></div><button class="file-action" data-action="${["offered", "offering"].includes(m.status) && !m.outgoing && m.kind === "file" ? "accept" : "media"}" data-id="${m.id}">${["offered", "offering"].includes(m.status) && !m.outgoing && m.kind === "file" ? "Nhận tệp" : "Mở"}</button>`;
          if (m.kind === "image" && media.has(m.id))
            content = `<button data-action="media" data-id="${m.id}"><img class="media-preview" src="${media.get(m.id).url}" alt="${esc(m.fileName)}"></button>`;
          const date =
            !prev ||
            new Date(prev.time).toDateString() !==
              new Date(m.time).toDateString()
              ? `<div class="day">${new Date(m.time).toLocaleDateString("vi-VN", { day: "numeric", month: "long" })}</div>`
              : "";
          return `${date}<article class="message ${m.outgoing ? "mine" : ""} ${grouped ? "join" : ""}" data-message="${m.id}"><div class="bubble" data-id="${m.id}">${!m.outgoing && contact(current)?.isGroup && !grouped ? `<div class="sender">${esc(m.senderName)}</div>` : ""}${m.replyToId ? `<div class="quote"><strong>${esc(quoted?.senderName || "Trả lời tin nhắn")}</strong>${esc(quoted?.text || quoted?.fileName || "Tin nhắn trước đó")}</div>` : ""}${content}${tail || m._pending ? `<div class="stamp"><time>${esc(clock(m.time))}</time>${m.outgoing ? `<span>${esc(status)}</span>` : ""}</div>` : ""}${m.expiresAt ? `<div class="stamp">Tự hủy · ${Math.max(1, Math.ceil((Date.parse(m.expiresAt) - Date.now()) / 1000))}s</div>` : ""}</div>${counts ? `<button class="reaction-count" data-action="react" data-id="${m.id}">${esc(counts)}</button>` : ""}${m._failed ? `<button class="retry" data-action="retry" data-id="${m.id}">Chạm để thử gửi lại</button>` : ""}</article>`;
        })
        .join("")
    : '<div class="empty">Chưa có tin nhắn. Gửi lời chào nhé.</div>';
  box.scrollTop = bottom || nearBottom ? box.scrollHeight : oldTop;
  renderSelection();
  markRead();
  for (const m of items)
    if (
      mediaJobs.size < 2 &&
      autoMediaAttempted.get(m.id) !== m.status &&
      m.kind === "image" &&
      !media.has(m.id) &&
      !mediaJobs.has(m.id) &&
      m.fileSize <= 8 * 1024 * 1024 &&
      connection === "online"
    )
      download(m, true)
        .then(() => {
          lastRender = "";
          renderMessages();
        })
        .catch(() => {});
}

const colorThemes = [
  {
    id: "jade",
    name: "Ngọc bích",
    accent: "#087f78",
    tint: "#e0f1ec",
    bubble: "#d9eee6",
    dark: "#54c6b2",
    darkTint: "#223f3e",
    darkBubble: "#214b46",
  },
  {
    id: "ocean",
    name: "Đại dương",
    accent: "#1764b0",
    tint: "#e2eeff",
    bubble: "#dbeafe",
    dark: "#78b6ff",
    darkTint: "#1b3453",
    darkBubble: "#203d5e",
  },
  {
    id: "violet",
    name: "Tử đinh hương",
    accent: "#7550ab",
    tint: "#f0e7fc",
    bubble: "#eaddf8",
    dark: "#c0a0f5",
    darkTint: "#362b4c",
    darkBubble: "#423357",
  },
  {
    id: "rose",
    name: "Hồng trà",
    accent: "#a63c68",
    tint: "#fbe7ee",
    bubble: "#f8dfe8",
    dark: "#f09abd",
    darkTint: "#472837",
    darkBubble: "#542f40",
  },
  {
    id: "amber",
    name: "Hổ phách",
    accent: "#956015",
    tint: "#fff0d6",
    bubble: "#fae7c4",
    dark: "#eac071",
    darkTint: "#443621",
    darkBubble: "#514028",
  },
];
function applyColorTheme() {
  const t =
      colorThemes.find((x) => x.id === state?.settings.colorTheme) ||
      colorThemes[0],
    dark = matchMedia("(prefers-color-scheme: dark)").matches;
  for (const [key, value] of Object.entries({
    "--accent": dark ? t.dark : t.accent,
    "--tint": dark ? t.darkTint : t.tint,
    "--own": dark ? t.darkBubble : t.bubble,
  }))
    document.documentElement.style.setProperty(key, value);
}
matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => {
  if (state) applyColorTheme();
});
function selectMessages(enabled, id) {
  selecting = enabled;
  selectedMessages.clear();
  if (id) selectedMessages.add(id);
  renderSelection();
}
function renderSelection() {
  if (!$("#selection-bar")) return;
  $("#selection-bar").hidden = !selecting;
  $("#selection-count").textContent = selectedMessages.size + " tin đã chọn";
  $("#delete-selected").disabled = !selectedMessages.size;
  for (const row of document.querySelectorAll("[data-message]")) {
    row.classList.toggle("selectable", selecting);
    row.classList.toggle("selected", selectedMessages.has(row.dataset.message));
    row.setAttribute(
      "aria-selected",
      String(selectedMessages.has(row.dataset.message)),
    );
  }
}
function confirmDelete(ids) {
  const conversation = current;
  if (!conversation || isLocked(conversation)) return;
  if (!client?.ready) {
    toast("Kết nối PC để xóa và đồng bộ lịch sử.");
    return;
  }
  showSheet(
    ids ? "Xóa " + ids.length + " tin nhắn?" : "Dọn lịch sử hội thoại?",
    `<p>Xóa khỏi lịch sử của bạn và điện thoại đã ghép đôi. Bản ở máy đối tác vẫn còn. Không thể hoàn tác.</p><form id="delete-form">${ids ? "" : '<label>Nhập XÓA để xác nhận<input id="delete-phrase" autocomplete="off" aria-label="Xác nhận dọn lịch sử"></label>'}<button class="danger">Xóa lịch sử</button><p class="error" id="delete-error"></p></form>`,
  );
  $("#delete-form").onsubmit = async (e) => {
    e.preventDefault();
    if (!ids && $("#delete-phrase").value.trim() !== "XÓA") {
      $("#delete-error").textContent = "Nhập đúng XÓA để xác nhận.";
      return;
    }
    if (current !== conversation || isLocked(conversation)) return;
    const button = e.target.querySelector("button");
    button.disabled = true;
    try {
      const packet = await client.request({
        type: ids ? "delete-messages" : "clear-history",
        conversation,
        messageIds: ids || [],
      });
      state.outbox = state.outbox.filter(
        (m) =>
          m.conversationId !== conversation || (ids && !ids.includes(m.id)),
      );
      clearMedia(conversation);
      // A newer push may already contain the post-delete snapshot. Do not wipe
      // it before update() rejects an older command response by serial number.
      replyTo = null;
      $("#reply-banner").hidden = true;
      update(packet);
      selectMessages(false);
      closeSheet();
      await save();
      toast("Đã xóa khỏi lịch sử của bạn.");
    } catch (error) {
      if ($("#delete-error")) $("#delete-error").textContent = error.message;
      button.disabled = false;
    }
  };
}
