import SwiftUI
import AVFoundation

struct QRScanner: UIViewControllerRepresentable {
    let scanned: (String) -> Void
    func makeUIViewController(context: Context) -> ScannerController { let controller = ScannerController(); controller.scanned = scanned; return controller }
    func updateUIViewController(_ uiViewController: ScannerController, context: Context) {}
    static func dismantleUIViewController(_ uiViewController: ScannerController, coordinator: ()) { uiViewController.stop() }
}

final class ScannerController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    var scanned: ((String) -> Void)?
    private let session = AVCaptureSession()
    private let queue = DispatchQueue(label: "lantern.camera")
    private var preview: AVCaptureVideoPreviewLayer?
    private var finished = false
    private var disposed = false
    private let hint = UILabel()
    override func viewDidLoad() {
        super.viewDidLoad(); view.backgroundColor = .black
        hint.text = "Đưa mã QR trên PC vào khung hình"; hint.textColor = .white; hint.textAlignment = .center; hint.numberOfLines = 0
        view.addSubview(hint)
        AVCaptureDevice.requestAccess(for: .video) { [weak self] granted in
            DispatchQueue.main.async {
                guard let self, !self.disposed else { return }
                if granted { self.configure() } else { self.hint.text = "Chưa có quyền Camera. Cho phép trong Cài đặt → Lantern, hoặc quay lại và dán nội dung QR." }
            }
        }
    }
    private func configure() {
        guard let device = AVCaptureDevice.default(for: .video), let input = try? AVCaptureDeviceInput(device: device), session.canAddInput(input) else { hint.text = "Không mở được camera."; return }
        let output = AVCaptureMetadataOutput()
        session.beginConfiguration(); session.addInput(input)
        guard session.canAddOutput(output) else { session.commitConfiguration(); hint.text = "Camera không hỗ trợ QR."; return }
        session.addOutput(output); output.setMetadataObjectsDelegate(self, queue: .main); output.metadataObjectTypes = [.qr]; session.commitConfiguration()
        let layer = AVCaptureVideoPreviewLayer(session: session); layer.videoGravity = .resizeAspectFill; view.layer.insertSublayer(layer, at: 0); preview = layer
        view.setNeedsLayout(); queue.async { [session] in session.startRunning() }
    }
    override func viewDidLayoutSubviews() { super.viewDidLayoutSubviews(); preview?.frame = view.bounds; hint.frame = CGRect(x: 24, y: view.safeAreaInsets.top + 24, width: view.bounds.width - 48, height: 100) }
    func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        guard !finished, let value = (metadataObjects.first as? AVMetadataMachineReadableCodeObject)?.stringValue else { return }
        finished = true; stop(); scanned?(value)
    }
    func stop() { disposed = true; queue.async { [session] in session.stopRunning() } }
}
