// Keep the camera's detail: a dense PC pairing QR can be unreadable after scaling
// an entire portrait frame down. Try the centre at native resolution as well.
export function scanRegions(width, height, photo = false) {
  if (width < 1 || height < 1) return [];
  const edge = Math.round(Math.min(width, height) * 0.88);
  return [
    { x: Math.floor((width - edge) / 2), y: Math.floor((height - edge) / 2), width: edge, height: edge, limit: 1280 },
    { x: 0, y: 0, width, height, limit: photo ? 2200 : 1600 },
    { x: 0, y: 0, width, height, limit: 800 },
  ];
}

export class QrDecoder {
  constructor({ canvas = document.createElement('canvas'), jsQR = globalThis.jsQR, detector = globalThis.BarcodeDetector } = {}) {
    this.canvas = canvas;
    this.context = canvas.getContext('2d', { willReadFrequently: true });
    this.jsQR = jsQR;
    try { this.native = detector ? new detector({ formats: ['qr_code'] }) : null; } catch { this.native = null; }
    if (!this.context || (!this.native && typeof jsQR !== 'function'))
      throw Error('Chưa tải được bộ đọc QR. Đóng và mở lại Lantern hoặc kiểm tra cập nhật.');
  }
  async read(source, photo = false) {
    const width = source.videoWidth || source.naturalWidth || source.width;
    const height = source.videoHeight || source.naturalHeight || source.height;
    if (!width || !height) return null;
    if (this.native) {
      try {
        const found = await this.native.detect(source);
        if (found[0]?.rawValue) return found[0].rawValue;
      } catch {
        // Some Safari versions expose this API without implementing QR detection.
        this.native = null;
        if (typeof this.jsQR !== 'function') throw Error('Bộ đọc QR không khả dụng. Kiểm tra cập nhật Lantern.');
      }
    }
    if (typeof this.jsQR !== 'function') return null;
    for (const region of scanRegions(width, height, photo)) {
      const scale = Math.min(1, region.limit / Math.max(region.width, region.height));
      this.canvas.width = Math.max(1, Math.round(region.width * scale));
      this.canvas.height = Math.max(1, Math.round(region.height * scale));
      this.context.drawImage(source, region.x, region.y, region.width, region.height, 0, 0, this.canvas.width, this.canvas.height);
      const data = this.context.getImageData(0, 0, this.canvas.width, this.canvas.height);
      const code = this.jsQR(data.data, data.width, data.height, { inversionAttempts: 'attemptBoth' });
      if (code?.data) return code.data;
    }
    return null;
  }
}

export function cameraError(error) {
  if (error?.name === 'NotAllowedError') return 'Camera chưa được cho phép. Cho phép camera trong Safari hoặc chọn ảnh QR.';
  if (error?.name === 'NotFoundError') return 'Không tìm thấy camera. Đại ca có thể chọn ảnh QR từ thư viện.';
  if (error?.name === 'NotReadableError') return 'Camera đang bận. Đóng ứng dụng đang dùng camera rồi thử lại.';
  return error?.message || 'Chưa mở được camera. Thử lại hoặc chọn ảnh QR.';
}

export class CameraScanner {
  constructor(video, { onCode, onStatus, onError }, { mediaDevices = navigator.mediaDevices, decoder = new QrDecoder(), schedule = (fn, delay) => setTimeout(fn, delay), cancel = id => clearTimeout(id), now = () => Date.now() } = {}) {
    Object.assign(this, { video, onCode, onStatus, onError, mediaDevices, decoder, schedule, cancel, now });
    this.active = false;
  }
  async start() {
    if (this.active) return;
    this.active = true;
    this.started = this.now();
    this.onStatus('Đang mở camera…');
    try {
      if (!this.mediaDevices?.getUserMedia) throw Error('Cần mở Lantern bằng Safari qua HTTPS để dùng camera.');
      const stream = await this.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: 'environment' }, width: { ideal: 1920 }, height: { ideal: 1080 } }, audio: false,
      });
      if (!this.active) { stream.getTracks().forEach(track => track.stop()); return; }
      this.stream = stream;
      this.video.muted = true;
      this.video.playsInline = true;
      this.video.autoplay = true;
      this.video.srcObject = stream;
      const track = stream.getVideoTracks()[0];
      if (track?.getCapabilities?.().focusMode?.includes('continuous'))
        track.applyConstraints({ advanced: [{ focusMode: 'continuous' }] }).catch(() => {});
      await this.video.play();
      if (!this.active) return;
      this.onStatus('Đang quét · Giữ trọn mã QR trong khung');
      await this.tick();
    } catch (error) { this.fail(error); }
  }
  async tick() {
    if (!this.active) return;
    try {
      if (this.video.readyState >= 2 && this.video.videoWidth > 0 && this.video.videoHeight > 0) {
        this.hasFrame = true;
        const code = await this.decoder.read(this.video);
        if (!this.active) return;
        if (code && (code !== this.lastCode || this.now() - this.lastCodeAt > 2500)) {
          this.lastCode = code; this.lastCodeAt = this.now();
          if (await this.onCode(code)) { this.stop(); return; }
        } else if (!this.lastCode && this.now() - this.started > 6500) {
          this.onStatus('Chưa thấy mã rõ. Đưa máy gần hơn, tránh lóa hoặc chọn ảnh QR.');
        }
      } else if (!this.hasFrame && this.now() - this.started > 10000) {
        throw Error('Camera chưa có hình. Thử mở lại camera hoặc chọn ảnh QR.');
      }
      if (this.active) this.timer = this.schedule(() => this.tick(), 180);
    } catch (error) { this.fail(error); }
  }
  fail(error) { if (!this.active) return; this.stop(); this.onError(cameraError(error)); }
  stop() {
    this.active = false;
    this.cancel(this.timer);
    this.stream?.getTracks().forEach(track => track.stop());
    this.stream = null;
    this.video.pause();
    this.video.srcObject = null;
  }
}

export async function readQrPhoto(file) {
  if (!file || file.size > 25 * 1024 * 1024) throw Error('Chọn ảnh QR nhỏ hơn 25 MB.');
  const url = URL.createObjectURL(file);
  try {
    const image = new Image();
    image.src = url;
    await image.decode();
    const value = await new QrDecoder().read(image, true);
    if (!value) throw Error('Chưa đọc được QR trong ảnh. Chọn ảnh rõ, có đủ bốn góc của mã.');
    return value;
  } finally { URL.revokeObjectURL(url); }
}
