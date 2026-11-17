// Synthetic HTTPS packet relay for iOS simulator transport tests. No production credentials.
import https from 'node:https';
import net from 'node:net';
import fs from 'node:fs';
const [certificate, privateKey] = process.argv.slice(2);
const routes = new Map();
const server = https.createServer({ cert: fs.readFileSync(certificate), key: fs.readFileSync(privateKey) }, async (request, response) => {
  const reply = (status, data) => { response.writeHead(status, { 'content-type': 'application/json' }); response.end(JSON.stringify(data)); };
  if (request.headers.authorization !== `Bearer ${'b'.repeat(43)}` || request.headers['oai-sites-authorization'] !== 'Bearer fixture-only') return reply(401, {});
  let raw = '';
  for await (const bytes of request) { raw += bytes.toString(); if (raw.length > 100000) return reply(413, {}); }
  let body; try { body = JSON.parse(raw); } catch { return reply(400, {}); }
  if (request.url === '/fixture/reset') {
    for (const route of routes.values()) route.socket.destroy();
    routes.clear(); return reply(200, {});
  }
  if (request.url === `/v1/routes/${'a'.repeat(32)}/open`) {
    for (const route of routes.values()) route.socket.destroy();
    routes.clear();
    const socket = net.connect(45854, '127.0.0.1');
    const route = { socket, packets: [], next: 0, sent: 0, last: '', closed: false };
    socket.on('data', data => {
      for (let offset = 0; offset < data.length; offset += 32768) route.packets.push({ seq: ++route.next, data: data.subarray(offset, offset + 32768).toString('base64') });
    });
    socket.on('error', () => { route.closed = true; });
    socket.on('close', () => { route.closed = true; });
    routes.set(body.session, route);
    return reply(200, { session: body.session });
  }
  const route = routes.get(body.session);
  if (!route || route.closed) return reply(409, {});
  if (request.url !== `/v1/routes/${'a'.repeat(32)}/exchange`) return reply(404, {});
  route.packets = route.packets.filter(packet => packet.seq > body.after);
  if (body.data) {
    if (body.seq === route.sent + 1) { route.socket.write(Buffer.from(body.data, 'base64')); route.sent = body.seq; route.last = body.data; }
    else if (body.seq !== route.sent || body.data !== route.last) return reply(409, {});
  }
  return reply(200, { session: body.session, packets: body.pushOnly ? [] : route.packets.slice(0, body.batch === 4 ? 4 : 1) });
});
server.listen(45855, '127.0.0.1', () => process.stdout.write('relay ready\n'));
process.on('SIGTERM', () => { for (const route of routes.values()) route.socket.destroy(); server.close(); });
