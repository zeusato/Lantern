import {escapeHtml} from './emoji.js';
export function senderAvatars(messages){
  const result=new Map();for(const message of messages)if(!message.outgoing&&typeof message.senderAvatar==='string')result.set(message.senderId,message.senderAvatar);
  return result;
}
export function avatarHtml(name,data){
  const label=escapeHtml(name||'Người gửi');
  const image=typeof data==='string'&&data.length<=218456&&/^iVBORw0KGgo[A-Za-z0-9+/]*={0,2}$/.test(data);
  return `<span class="message-avatar" role="img" aria-label="Ảnh đại diện ${label}" title="${label}">${image?`<img src="data:image/png;base64,${data}" alt="">`:escapeHtml(Array.from((name||'?').trim())[0]?.toLocaleUpperCase('vi-VN')||'?')}</span>`;
}
