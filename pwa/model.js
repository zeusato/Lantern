export const alive = (message, now = Date.now()) =>
  !message.expiresAt || Date.parse(message.expiresAt) > now;
export function joins(a, b) {
  if (
    !a ||
    !b ||
    !a.senderId ||
    a.senderId !== b.senderId ||
    a.conversationId !== b.conversationId ||
    a.outgoing !== b.outgoing
  )
    return false;
  const x = new Date(a.time),
    y = new Date(b.time);
  return y - x >= 0 && y - x <= 300000 && x.toDateString() === y.toDateString();
}
export function reconcile(history, outbox, conversation) {
  const ids = new Set(history.map((x) => x.id));
  return [
    ...history,
    ...outbox.filter(
      (x) => x.conversationId === conversation && !ids.has(x.id),
    ),
  ]
    .filter((x) => alive(x))
    .sort((a, b) => new Date(a.time) - new Date(b.time));
}
export function applySnapshot(previous, response) {
  if (response.type === "unchanged") return previous.filter((x) => alive(x));
  return (response.messages || []).filter((x) => alive(x));
}
export function invalidateDeletedHistory(state, conversations) {
  const changed = [];
  const visible = new Set(conversations.map(c => c.id));
  const blocked = new Set([
    ...state.conversations.filter(c => !visible.has(c.id)).map(c => c.id),
  ]);
  for (const id of blocked) {
    delete state.histories[id];
    if (state.hashes) delete state.hashes[id];
    if (state.boxes) delete state.boxes[id];
    if (state.drafts) delete state.drafts[id];
    if (state.outbox) state.outbox = state.outbox.filter(m => m.conversationId !== id);
    changed.push(id);
  }
  for (const next of conversations) {
    if (blocked.has(next.id)) continue;
    const previous = state.conversations.find((c) => c.id === next.id);
    if (
      !previous ||
      (next.historyVersion || 0) <= (previous.historyVersion || 0)
    )
      continue;
    delete state.histories[next.id];
    if (state.hashes) delete state.hashes[next.id];
    // Pending drafts/messages have their own encrypted box and survive history deletion.
    if (state.boxes[next.id]) delete state.boxes[next.id].box;
    changed.push(next.id);
  }
  return changed;
}
export function emoticons(value) {
  const map = {
    ":)": "🙂",
    ":-)": "🙂",
    ":D": "😄",
    ":-D": "😄",
    ";)": "😉",
    ":(": "🙁",
    ":-(": "🙁",
    "<3": "❤️",
    ":P": "😛",
  };
  return value.replace(
    /(^|\s)(:-?\)|:-?D|;\)|:-?\(|<3|:P)(?=\s|$)/g,
    (_, before, match) => before + map[match],
  );
}
