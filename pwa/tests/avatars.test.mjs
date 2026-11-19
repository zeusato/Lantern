import test from 'node:test';
import assert from 'node:assert/strict';
import {avatarHtml,senderAvatars} from '../avatars.js';

test('incoming author profiles are shared across their messages and removal clears the image',()=>{
  const map=senderAvatars([{senderId:'a'}, {senderId:'a',senderAvatar:'photo'}, {senderId:'own',outgoing:true,senderAvatar:'private'}]);
  assert.equal(map.get('a'),'photo'); assert.equal(map.has('own'),false);
  assert.equal(senderAvatars([{senderId:'a',senderAvatar:'photo'},{senderId:'a',senderAvatar:''}]).get('a'),'');
});
test('avatar markup escapes names and accepts only bounded PNG data',()=>{
  assert.ok(avatarHtml('An','iVBORw0KGgoAAA==').includes('data:image/png;base64,'));
  assert.ok(!avatarHtml('<script>','https://example.com/a.png').includes('<img'));
  assert.ok(!avatarHtml('A','iVBORw0KGgo'+'A'.repeat(220000)).includes('<img'));
  assert.ok(!avatarHtml('A','iVBORw0KGgo" onerror="bad').includes('<img'));
  assert.ok(avatarHtml('"<script>','').includes('&lt;script&gt;'));
});
