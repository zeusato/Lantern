// Read only the installed app's emoji assets and mapping tables; never execute archive code.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const source = process.argv[2];
if (!source) throw new Error('Usage: node scripts/Import-ZaloEmoji.mjs path/to/resources/app.asar');
const fd = fs.openSync(source, 'r');
try {
  const prefix = Buffer.alloc(16); fs.readSync(fd, prefix, 0, 16, 0);
  const headerSize = prefix.readUInt32LE(4), jsonSize = prefix.readUInt32LE(12);
  if (headerSize > 50_000_000 || jsonSize > headerSize) throw new Error('Invalid ASAR header');
  const headerBytes = Buffer.alloc(jsonSize); fs.readSync(fd, headerBytes, 0, jsonSize, 16);
  const header = JSON.parse(headerBytes.toString('utf8'));
  function read(name) {
    let item = header; for (const part of name.split('/')) item = item.files?.[part];
    if (!item || item.files || item.unpacked || item.size > 2_000_000) throw new Error('Invalid asset: ' + name);
    const bytes = Buffer.alloc(item.size); fs.readSync(fd, bytes, 0, bytes.length, 8 + headerSize + Number(item.offset)); return bytes;
  }
  const data = JSON.parse(read('pc-dist/emoji/emoji-data.json'));
  const positions = JSON.parse(read('pc-dist/emoji/emoji-rc.json'));
  const normalize = value => value.replaceAll('\uFE0F', '');
  const names = JSON.parse(fs.readFileSync('scripts/emoji-names.vi.json', 'utf8'));
  const unicodeAliases = { ':)':'🙂', ':d':'😃', ":')":'😂', '8-)':'😎', ':-((':'😭', ':b':'😍', ':p':'😛', ':o':'😮', ':(':'🙁', ';-)':'😉', ':>':'🤣', ':-h':'😡', ':((':'😢', ':-*':'😘', ':|':'😐', '/-heart':'❤️', '/-break':'💔', '/-strong':'👍', '/-weak':'👎', '/-cake':'🎂', '/-rose':'🌹', '/-beer':'🍺', '/-coffee':'☕', '/-shit':'💩', '/-v':'✌️', '/-ok':'👌', '_()_':'🙏' };
  const customNames = { ':)':'Mỉm cười', ':d':'Cười tươi', ":')":'Cười rơi nước mắt', ':b':'Mê quá', ':))':'Cười khoái chí', ':>':'Cười lớn', ':v':'Cười há miệng', '8-)':'Ngầu', ':-h':'Tức giận', ':-((':'Khóc lớn', ':((':'Khóc', ':p':'Lè lưỡi', ':(':'Buồn', ':o':'Ngạc nhiên', ';-)':'Nháy mắt', ':$':'Ngượng ngùng', ':3':'Dễ thương', ':z':'Buồn ngủ', '/-heart':'Trái tim', '/-strong':'Thích', '/-weak':'Không thích', '/-cake':'Sinh nhật', '/-coffee':'Cà phê', '/-beer':'Bia', '/-rose':'Hoa hồng', '/-break':'Tan vỡ', '_()_':'Cảm ơn' };
  const customCodes = new Set(Object.entries(data).filter(([key, value]) => key === value && /^[\x00-\x7F]+$/.test(key)).map(([key]) => key));
  const catalog = [], copied = new Map(), customByPosition = new Map();
  const destination = 'src/Lantern.App/Assets/Emoji/Zalo'; fs.mkdirSync(destination, {recursive:true});
  function asset(code, name, aliases, category) {
    const coordinates = positions[code]; if (!coordinates) throw new Error('Missing coordinates: ' + code);
    const [column, row] = coordinates.split(' ').map(Number);
    if (!Number.isInteger(row) || !Number.isInteger(column) || row < 1 || column < 1) throw new Error('Invalid coordinates');
    const filename = `row-${row}-column-${column}.png`, member = 'pc-dist/emoji/emoji-64/' + filename;
    if (!copied.has(filename)) { const bytes = read(member); if (bytes.toString('hex', 0, 8) !== '89504e470d0a1a0a') throw new Error('Invalid PNG'); fs.writeFileSync(path.join(destination,filename),bytes); copied.set(filename,crypto.createHash('sha256').update(bytes).digest('hex')); }
    const entry = {Emoji:code,Name:name,File:'Zalo/'+filename,Source:member,Shortcode:aliases[0]??code,Aliases:[...new Set(aliases)],Category:category}; catalog.push(entry); return entry;
  }
  for (const [code, coordinate] of Object.entries(positions)) {
    if (!customCodes.has(code)) continue;
    if (customByPosition.has(coordinate)) { customByPosition.get(coordinate).Aliases.push(code); continue; }
    const aliases = [code, ...(unicodeAliases[code] ? [unicodeAliases[code]] : [])];
    customByPosition.set(coordinate,asset(code,customNames[code]??('Biểu cảm '+code),aliases,'Cảm xúc Zalo'));
  }
  for (const [alias, coordinate] of Object.entries(positions)) {
    const entry = customByPosition.get(coordinate);
    if (entry && /^[\x00-\x7F]+$/.test(alias) && !entry.Aliases.includes(alias)) entry.Aliases.push(alias);
  }
  const overridden = new Set(Object.values(unicodeAliases).map(normalize));
  const byUnicode = new Map();
  function category(emoji) { const cp=emoji.codePointAt(0); if(cp>=0x1f1e6&&cp<=0x1f1ff)return 'Cờ'; if(cp>=0x1f600&&cp<=0x1f64f||cp>=0x1f440&&cp<=0x1f487||cp>=0x1f910&&cp<=0x1f9ff)return 'Cảm xúc & con người'; if(cp>=0x1f32d&&cp<=0x1f37f)return 'Đồ ăn'; if(cp>=0x1f400&&cp<=0x1f43f||cp>=0x1f300&&cp<=0x1f32c)return 'Thiên nhiên'; if(cp>=0x1f680&&cp<=0x1f6ff||cp>=0x1f3d0&&cp<=0x1f3ff)return 'Du lịch'; if(cp>=0x1f380&&cp<=0x1f3cf)return 'Hoạt động'; return 'Đồ vật & ký hiệu'; }
  for (const [shortcode, emoji] of Object.entries(data)) {
    if (customCodes.has(shortcode) || !positions[emoji]) continue;
    const key=normalize(emoji);
    if(overridden.has(key)) { const entry=catalog.find(item=>item.Aliases.some(alias=>normalize(alias)===key)); if(entry)entry.Aliases.push(shortcode); continue; }
    if(byUnicode.has(key)){byUnicode.get(key).Aliases.push(shortcode);continue;}
    byUnicode.set(key,asset(emoji,names[key]??shortcode.replace(/^:|:$/g,'').replaceAll('_',' '),[shortcode],category(emoji)));
  }
  const shortcodes = {};
  // Preserve existing Unicode messages while using the matching Zalo artwork.
  for(const entry of catalog) for(const alias of [entry.Emoji,...entry.Aliases]) if(/^[\x00-\x7F]+$/.test(alias)) shortcodes[alias.toLowerCase()]=unicodeAliases[entry.Emoji]??entry.Emoji;
  fs.writeFileSync('src/Lantern.App/Assets/Emoji/zalo-catalog.json',JSON.stringify(catalog,null,2)+'\n');
  fs.writeFileSync('src/Lantern.Core/emoji-shortcodes.json',JSON.stringify(shortcodes,null,2)+'\n');
  fs.writeFileSync('src/Lantern.App/Assets/Emoji/Zalo/source.json',JSON.stringify({source:'Zalo PC resources/app.asar',version:path.basename(path.dirname(path.dirname(source))),members:Object.fromEntries(copied)},null,2)+'\n');
  console.log(JSON.stringify({icons:catalog.length,images:copied.size,shortcodes:Object.keys(shortcodes).length,custom:customByPosition.size}));
} finally { fs.closeSync(fd); }
