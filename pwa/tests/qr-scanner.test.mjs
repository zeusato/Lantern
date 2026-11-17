import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import jsQR from 'jsqr';
import { QrDecoder, CameraScanner } from '../qr-scanner.js';
import { validatePairing } from '../channel.js';

const fixture = JSON.parse(readFileSync(new URL('./fixtures/pc-web-qr.json', import.meta.url)));
function raster({ inverted = false, rotate = false } = {}) {
  const width = 1920, height = 1080, edge = fixture.rows.length * 3;
  const pixels = new Uint8ClampedArray(width * height * 4).fill(inverted ? 0 : 255);
  for (let i = 3; i < pixels.length; i += 4) pixels[i] = 255;
  for (let y = 0; y < edge; y++) for (let x = 0; x < edge; x++) {
    const dark = fixture.rows[Math.floor(y / 3)][Math.floor(x / 3)] === '1';
    const px = Math.floor((width - edge) / 2) + (rotate ? edge - y - 1 : x);
    const py = Math.floor((height - edge) / 2) + (rotate ? x : y);
    const i = (py * width + px) * 4, c = dark !== inverted ? 0 : 255;
    pixels[i] = pixels[i + 1] = pixels[i + 2] = c;
  }
  return { width, height, pixels };
}
// Canvas pixel resampling with a real PC-generated symbol, not a mocked QR result.
function testCanvas() {
  const canvas = { width: 0, height: 0 };
  let image;
  canvas.getContext = () => ({
    drawImage(source, sx, sy, sw, sh, dx, dy, width, height) {
      image = new Uint8ClampedArray(width * height * 4);
      for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
        const offset = (Math.min(source.height - 1, Math.floor(sy + y * sh / height)) * source.width + Math.min(source.width - 1, Math.floor(sx + x * sw / width))) * 4;
        image.set(source.pixels.subarray(offset, offset + 4), (y * width + x) * 4);
      }
    },
    getImageData: () => ({ data: image, width: canvas.width, height: canvas.height }),
  });
  return canvas;
}
test('reads dense PC web QR in landscape camera, rotated and inverted frames', async () => {
  for (const options of [{}, { rotate: true }, { inverted: true }]) {
    const decoder = new QrDecoder({ canvas: testCanvas(), jsQR, detector: null });
    const code = await decoder.read(raster(options));
    assert.equal(code, fixture.payload);
    assert.equal(validatePairing(code, 'https://lantern-private-relay-zeusato.tranquymtcn.chatgpt.site').v, 2);
  }
});
test('recovers a dense QR that whole-frame 960px scaling loses', async () => {
  const image = raster(), canvas = testCanvas();
  canvas.width = 960; canvas.height = 540;
  const ctx = canvas.getContext(); ctx.drawImage(image, 0, 0, 1920, 1080, 0, 0, 960, 540);
  assert.equal(jsQR(ctx.getImageData().data, 960, 540, { inversionAttempts: 'dontInvert' }), null);
  assert.equal(await new QrDecoder({ canvas, jsQR, detector: null }).read(image), fixture.payload);
});
test('native detector failure falls back to jsQR; missing decoder is a visible failure', async () => {
  class Unsupported { async detect() { throw Error('unsupported'); } }
  const decoder = new QrDecoder({ canvas: testCanvas(), jsQR, detector: Unsupported });
  assert.equal(await decoder.read(raster()), fixture.payload);
  assert.throws(() => new QrDecoder({ canvas: testCanvas(), jsQR: null, detector: null }), /bộ đọc QR/);
});
test('wrong-app, unrelated and expired codes give specific retry guidance', () => {
  const origin = 'https://lantern-private-relay-zeusato.tranquymtcn.chatgpt.site';
  assert.throws(() => validatePairing('https://example.com/', origin), /không phải mã Lantern/);
  assert.throws(() => validatePairing({ v: 1 }, origin), /Android\/iOS/);
  assert.throws(() => validatePairing({ ...JSON.parse(fixture.payload), expires: 1 }, origin), /hết hạn/);
});
function cameraFixture(overrides = {}) {
  const stopped = [], codes = [], errors = [], statuses = [], timers = [];
  const track = { stop: () => stopped.push(true) }, stream = { getTracks: () => [track], getVideoTracks: () => [track] };
  const video = { readyState: 2, videoWidth: 1080, videoHeight: 1920, play: async () => {}, pause: () => {} };
  const options = { mediaDevices: { getUserMedia: async () => stream }, decoder: { read: async () => fixture.payload }, schedule: fn => (timers.push(fn), timers.length), cancel: () => {}, ...overrides };
  const scanner = new CameraScanner(video, { onCode: async code => (codes.push(code), true), onStatus: value => statuses.push(value), onError: error => errors.push(error) }, options);
  return { scanner, stream, video, stopped, codes, errors, statuses, timers };
}
test('closing scanner while permission is pending releases the eventual stream', async () => {
  let grant;
  const f = cameraFixture({ mediaDevices: { getUserMedia: () => new Promise(resolve => grant = resolve) } });
  const pending = f.scanner.start(); f.scanner.stop(); grant(f.stream); await pending;
  assert.equal(f.stopped.length, 1); assert.equal(f.codes.length, 0); assert.equal(f.video.srcObject, null);
});
test('one camera session emits one accepted code and releases camera immediately', async () => {
  const f = cameraFixture(); await Promise.all([f.scanner.start(), f.scanner.start()]);
  assert.equal(f.codes.length, 1); assert.equal(f.stopped.length, 1); assert.equal(f.scanner.active, false);
});
test('closing during decode suppresses stale results; frame errors stop with feedback', async () => {
  let finish;
  const f = cameraFixture({ decoder: { read: () => new Promise(resolve => finish = resolve) } });
  const pending = f.scanner.start(); await new Promise(resolve => setImmediate(resolve));
  f.scanner.stop(); finish(fixture.payload); await pending; assert.equal(f.codes.length, 0);
  const broken = cameraFixture({ decoder: { read: async () => { throw Error('Không đọc được hình camera.'); } } });
  await broken.scanner.start(); assert.match(broken.errors[0], /hình camera/); assert.equal(broken.stopped.length, 1);
});
test('a rejected QR keeps scanning so a fresh correct QR can be accepted', async () => {
  const f = cameraFixture(); let accepted = false;
  f.scanner.onCode = async () => accepted;
  await f.scanner.start(); assert.equal(f.scanner.active, true); assert.equal(f.stopped.length, 0);
  accepted = true; f.scanner.lastCodeAt -= 3000; await f.scanner.tick();
  assert.equal(f.scanner.active, false); assert.equal(f.stopped.length, 1);
});
