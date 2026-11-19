import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {catalog,replaceEmoticons,emojiHtml,insertEmoji} from '../emoji.js';
test('mobile uses the exact PC catalog and image bytes', async()=>{
  const pc=JSON.parse((await readFile(new URL('../../src/Lantern.App/Assets/Emoji/zalo-catalog.json',import.meta.url),'utf8')).replace(/^\uFEFF/,''));
  assert.deepEqual(catalog,pc);
  for(const item of catalog) assert.deepEqual(await readFile(new URL('../assets/emoji/'+item.File,import.meta.url)),await readFile(new URL('../../src/Lantern.App/Assets/Emoji/'+item.File,import.meta.url)));
});
test('shortcodes, Unicode, text safety and literal code/URLs',()=>{
  assert.equal(replaceEmoticons('Chào :D :heart: <3'),'Chào 😃 ❤️ ❤️');
  assert.equal(replaceEmoticons('x :)',false),'x :)');
  assert.equal(replaceEmoticons('x :) ',false),'x 🙂 ');
  for(const value of ['https://site/:heart:', '`:)`', '```\n:)\n```','file:heart:']) assert.equal(replaceEmoticons(value),value);
  assert.match(emojiHtml('🙂'), /emoji-alone.*row-8-column-42.png/);
  assert.match(emojiHtml('x :) y'), /alt=":\)"/);
  assert.doesNotMatch(emojiHtml('<script>alert(1)</script> `:)`'),/<img|<script/);
  assert.match(emojiHtml('❤️👍'),/<img.*<img/);
});
test('picker inserts at caret and preserves surrounding draft',()=>{
  const input={value:'abc def',selectionStart:3,selectionEnd:3,setRangeText(text,a,b){this.value=this.value.slice(0,a)+text+this.value.slice(b);},dispatchEvent(){}};
  insertEmoji(input,':)');assert.equal(input.value,'abc :) def');
});
