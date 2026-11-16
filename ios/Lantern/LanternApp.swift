import SwiftUI
import UniformTypeIdentifiers
import QuickLook
import PhotosUI

@main struct LanternApp: App {
    @StateObject private var store = ChatStore()
    @Environment(\.scenePhase) private var phase
    var body: some Scene {
        WindowGroup {
            RootView().environmentObject(store)
                .tint(Color(red: 0.12, green: 0.63, blue: 0.55))
                .onChange(of: phase) { store.setVisible($0 == .active) }
                .task { store.start() }
        }
    }
}

struct RootView: View {
    @EnvironmentObject private var store: ChatStore
    @State private var scanner = false
    @State private var settings = false
    @State private var qr = ""
    @State private var pasteQR = false
    var body: some View {
        ZStack {
            NavigationStack {
                Group {
                    if store.paired { contacts }
                    else { pairing }
                }
                .navigationTitle("Lantern")
                .toolbar { ToolbarItem(placement: .navigationBarTrailing) { Button { settings = true } label: { Image(systemName: "gearshape") }.accessibilityLabel("Cài đặt") } }
            }
            if !store.visible { Color(.systemBackground).ignoresSafeArea().overlay { Label("Lantern", systemImage: "lock.fill").font(.title2) }.zIndex(10) }
        }
        .sheet(isPresented: $scanner) { QRScanner { value in scanner = false; store.pair(value) } }
        .sheet(isPresented: $settings) { SettingsView() }
        .alert("Lantern", isPresented: Binding(get: { !store.error.isEmpty }, set: { if !$0 { store.error = "" } })) { Button("Đóng") { store.error = "" } } message: { Text(store.error) }
        .alert("Dán nội dung QR", isPresented: $pasteQR) {
            TextField("Nội dung JSON từ QR trên PC", text: $qr)
            Button("Ghép đôi") { store.pair(qr); qr = "" }
            Button("Hủy", role: .cancel) { qr = "" }
        } message: { Text("Chỉ dùng QR vừa tạo trong Lantern trên PC của bạn.") }
    }
    private var pairing: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                Image(systemName: "bubble.left.and.bubble.right.fill").font(.system(size: 48)).foregroundStyle(.tint).padding(.top, 28)
                Text("Mang cuộc trò chuyện\ntheo bên mình.").font(.largeTitle.bold())
                Text("Mở Lantern trên PC → Cài đặt → Điện thoại → Ghép đôi QR.").foregroundStyle(.secondary)
                if store.pairingCode.isEmpty {
                    Button { scanner = true } label: { Label("Quét QR trên PC", systemImage: "qrcode.viewfinder").frame(maxWidth: .infinity).padding(8) }.buttonStyle(.borderedProminent)
                    Button("Dán nội dung QR") { pasteQR = true }
                } else {
                    Text("Đối chiếu đủ 6 nhóm mã với PC, rồi chấp nhận trên PC.").font(.headline)
                    Text(store.pairingCode).font(.system(.title3, design: .monospaced)).textSelection(.enabled)
                    ProgressView(store.status)
                    Button("Hủy ghép đôi", role: .cancel) { store.forget() }
                }
                Label("PC cần chạy Lantern và không ngủ.", systemImage: "desktopcomputer").font(.footnote).foregroundStyle(.secondary)
                Text("Bản cài IPA này nhận tin khi đang mở. Khi quay lại, Lantern tự kết nối và đồng bộ với PC.").font(.footnote).foregroundStyle(.secondary)
            }.padding(24)
        }
    }
    private var contacts: some View {
        List {
            Section {
                HStack(spacing: 10) {
                    Circle().fill(store.connected ? Color.green : Color.secondary).frame(width: 8, height: 8)
                    VStack(alignment: .leading) { Text(store.pcName).font(.headline); Text(store.status).font(.caption).foregroundStyle(.secondary) }
                }.padding(.vertical, 4)
            }
            Section("Cuộc trò chuyện") {
                if store.conversations.isEmpty { Text("Các liên hệ và nhóm trên PC sẽ xuất hiện ở đây.").foregroundStyle(.secondary) }
                ForEach(store.conversations) { c in
                    NavigationLink(value: c.id) {
                        HStack(spacing: 12) {
                            ZStack { Circle().fill(Color.accentColor.opacity(0.12)).frame(width: 42, height: 42); Image(systemName: c.raw.bool("isGroup") ? "person.2.fill" : "person.fill").foregroundStyle(.tint) }
                            VStack(alignment: .leading, spacing: 4) { Text(c.name).font(.headline); Text(c.locked ? "Hội thoại đang khóa" : c.raw.string("presence") == "offline" ? "Ngoại tuyến" : "Sẵn sàng trò chuyện").font(.caption).foregroundStyle(.secondary) }
                            Spacer()
                            if c.locked { Image(systemName: "lock.fill").foregroundStyle(.secondary) }
                            if c.unread > 0 { Text(c.unread > 99 ? "99+" : "\(c.unread)").font(.caption.bold()).padding(6).background(Color.accentColor, in: Capsule()).foregroundStyle(.white) }
                        }.padding(.vertical, 4)
                    }
                }
            }
        }
        .navigationDestination(for: String.self) { id in ChatView(conversationID: id) }
        .refreshable { await store.sync() }
    }
}

struct ChatView: View {
    let conversationID: String
    @EnvironmentObject private var store: ChatStore
    @State private var draft = ""
    @State private var password = ""
    @State private var importing = false
    @State private var photo: PhotosPickerItem?
    @State private var focusRequest = 0
    var body: some View {
        VStack(spacing: 0) {
            Text(store.status).font(.caption).foregroundStyle(.secondary).padding(8)
            if store.locked {
                Spacer()
                Image(systemName: "lock.shield").font(.largeTitle).foregroundStyle(.tint)
                Text("Hội thoại được bảo vệ").font(.headline).padding(.top)
                SecureField("Mật khẩu hội thoại", text: $password).textFieldStyle(.roundedBorder).padding(.horizontal, 40).padding(.top)
                Button("Mở khóa") { let value = password; password = ""; Task { await store.unlock(value) } }.buttonStyle(.borderedProminent).disabled(!store.connected || password.isEmpty)
                Spacer()
            } else {
                history
                if let progress = store.transferProgress {
                    HStack { ProgressView(value: progress); Button("Hủy") { store.cancelTransfer() } }.padding(.horizontal).padding(.vertical, 6)
                }
                if let reply = store.replyTo {
                    HStack { Text("Trả lời: " + (reply.text.isEmpty ? reply.raw.string("fileName") : reply.text)).font(.caption).lineLimit(2); Spacer(); Button { store.replyTo = nil } label: { Image(systemName: "xmark.circle.fill") } }.padding(12).background(.thinMaterial)
                }
                HStack(alignment: .bottom, spacing: 10) {
                    PhotosPicker(selection: $photo, matching: .any(of: [.images, .videos])) { Image(systemName: "photo").font(.title3).frame(width: 30, height: 40) }.disabled(!store.connected || store.transferProgress != nil).accessibilityLabel("Chọn ảnh hoặc video")
                    Button { importing = true } label: { Image(systemName: "paperclip").font(.title3).frame(width: 36, height: 40) }.disabled(!store.connected || store.transferProgress != nil).accessibilityLabel("Gửi tệp, ảnh hoặc video")
                    MessageComposer(text: $draft, focusRequest: focusRequest).fixedSize(horizontal: false, vertical: true)
                    Button {
                        if store.send(draft) { draft = ""; focusRequest += 1 }
                    } label: { Image(systemName: "arrow.up.circle.fill").font(.system(size: 34)).frame(height: 40) }.disabled(draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty).accessibilityLabel("Gửi tin nhắn").accessibilityIdentifier("send")
                }.padding(.horizontal, 12).padding(.vertical, 8)
            }
        }
        .navigationTitle(store.current?.name ?? "Trò chuyện")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar { if store.current?.protected == true && !store.locked { ToolbarItem(placement: .navigationBarTrailing) { Button { Task { await store.lock() } } label: { Image(systemName: "lock") }.accessibilityLabel("Khóa hội thoại") } } }
        .onAppear { store.select(conversationID) }
        .onChange(of: store.locked) { if !$0 { focusRequest += 1 } }
        .onDisappear { password = ""; draft = ""; store.select("") }
        .onChange(of: store.visible) { if !$0 { password = ""; if store.current?.protected == true { draft = "" } } }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.item]) { result in
            switch result { case .success(let url): store.upload(url); case .failure(let error): store.error = error.localizedDescription }
        }
        .onChange(of: photo) { item in
            guard let item else { return }
            Task {
                do { if let media = try await item.loadTransferable(type: ImportedMedia.self), store.visible, !store.locked, store.selected == conversationID { store.upload(media.url) } }
                catch { store.error = error.localizedDescription }
                photo = nil
            }
        }
        .sheet(isPresented: Binding(get: { store.preview != nil && store.visible && !store.locked }, set: { if !$0 { store.preview = nil } })) {
            if let url = store.preview { FilePreview(url: url).ignoresSafeArea() }
        }
    }
    private var history: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(spacing: 10) {
                    if store.messages.isEmpty { Text("Chưa có tin nhắn").foregroundStyle(.secondary).padding(40) }
                    ForEach(store.messages) { message in
                        MessageRow(message: message)
                            .id(message.id)
                            .onAppear { store.displayed(message, true) }
                            .onDisappear { store.displayed(message, false) }
                    }
                    Color.clear.frame(height: 1).id("end")
                }.padding(12)
            }
            .scrollDismissesKeyboard(.interactively)
            .onChange(of: store.messages.last?.id) { _ in proxy.scrollTo("end", anchor: .bottom) }
            .task { try? await Task.sleep(nanoseconds: 300_000_000); proxy.scrollTo("end", anchor: .bottom) }
        }
    }
}

// Keep the editor's native identity and selection stable while history/ACK updates redraw SwiftUI.
struct MessageComposer: UIViewRepresentable {
    @Binding var text: String
    let focusRequest: Int
    func makeCoordinator() -> Coordinator { Coordinator(self) }
    func makeUIView(context: Context) -> UITextView {
        let view = UITextView(); view.delegate = context.coordinator
        view.font = .preferredFont(forTextStyle: .body); view.adjustsFontForContentSizeCategory = true
        view.backgroundColor = .secondarySystemBackground; view.layer.cornerRadius = 18
        view.textContainerInset = UIEdgeInsets(top: 10, left: 8, bottom: 10, right: 8)
        view.accessibilityIdentifier = "composer"; view.accessibilityLabel = "Nhắn tin"
        view.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        return view
    }
    func updateUIView(_ view: UITextView, context: Context) {
        context.coordinator.parent = self
        if view.text != text { view.text = text }
        if context.coordinator.focusRequest != focusRequest {
            context.coordinator.focusRequest = focusRequest
            DispatchQueue.main.async { if view.window != nil { view.becomeFirstResponder() } }
        }
    }
    func sizeThatFits(_ proposal: ProposedViewSize, uiView: UITextView, context: Context) -> CGSize? {
        let width = proposal.width ?? 220
        let measured = uiView.sizeThatFits(CGSize(width: width, height: .greatestFiniteMagnitude))
        return CGSize(width: width, height: min(130, max(42, measured.height)))
    }
    static func dismantleUIView(_ uiView: UITextView, coordinator: Coordinator) { uiView.resignFirstResponder(); uiView.delegate = nil }
    final class Coordinator: NSObject, UITextViewDelegate {
        var parent: MessageComposer
        var focusRequest = 0
        init(_ parent: MessageComposer) { self.parent = parent }
        func textViewDidChange(_ textView: UITextView) { parent.text = textView.text; textView.invalidateIntrinsicContentSize() }
    }
}

struct ImportedMedia: Transferable {
    let url: URL
    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(importedContentType: .data) { received in
            let size = try received.file.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
            guard size <= Wire.maxFile else { throw LanternError.message("Ảnh/video tối đa 100 MB.") }
            let directory = FileManager.default.temporaryDirectory.appendingPathComponent("LanternMedia", isDirectory: true).appendingPathComponent(Wire.id(), isDirectory: true)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let target = directory.appendingPathComponent(received.file.lastPathComponent)
            try FileManager.default.copyItem(at: received.file, to: target)
            try FileManager.default.setAttributes([.protectionKey: FileProtectionType.complete], ofItemAtPath: target.path)
            return ImportedMedia(url: target)
        }
    }
}

struct MessageRow: View {
    let message: Message
    @EnvironmentObject private var store: ChatStore
    var body: some View {
        HStack {
            if message.outgoing { Spacer(minLength: 45) }
            VStack(alignment: .leading, spacing: 6) {
                if !message.outgoing && !message.raw.string("senderName").isEmpty { Text(message.raw.string("senderName")).font(.caption.bold()).foregroundStyle(.secondary) }
                if !message.raw.string("replyToId").isEmpty { Label("Trả lời tin nhắn", systemImage: "arrowshape.turn.up.left").font(.caption).foregroundStyle(.secondary) }
                if message.kind == "text" { Text(message.text).textSelection(.enabled) }
                else if ["image", "video", "file"].contains(message.kind) {
                    Button { store.download(message) } label: {
                        Label { VStack(alignment: .leading) { Text(message.raw.string("fileName")).lineLimit(2); Text(ByteCountFormatter.string(fromByteCount: Int64(message.raw.int("fileSize")), countStyle: .file)).font(.caption) } } icon: { Image(systemName: message.kind == "image" ? "photo" : message.kind == "video" ? "video" : "doc") }
                    }.disabled(!store.connected || store.transferProgress != nil)
                } else { Text(message.text.isEmpty ? "Nội dung: " + message.kind : message.text) }
                if let reactions = message.raw["reactions"] as? [String: String], !reactions.isEmpty { Text(reactions.values.sorted().joined(separator: " ")).font(.caption) }
                HStack(spacing: 5) {
                    if let time = Wire.date(message.raw.string("time")) { Text(time, style: .time) }
                    if message.outgoing {
                        if message.pending {
                            Button(message.raw.string("status") == "retry" ? "Chạm để gửi lại" : "Đang gửi…") { Task { await store.retry(message.id) } }.disabled(message.raw.string("status") != "retry")
                        } else { Text(message.raw.string("status") == "read" ? "Đã đọc" : message.raw.string("status") == "delivered" ? "Đã nhận" : "Đã gửi PC") }
                    }
                    if message.ephemeral { Image(systemName: "timer") }
                }.font(.caption2).foregroundStyle(.secondary)
            }
            .padding(12)
            .background(message.outgoing ? Color.accentColor.opacity(0.16) : Color(.secondarySystemBackground), in: RoundedRectangle(cornerRadius: 16))
            .contextMenu {
                if !message.pending {
                    Button { store.replyTo = message } label: { Label("Trả lời", systemImage: "arrowshape.turn.up.left") }
                    ForEach(["👍", "❤️", "😂", "😮", "😢", ""], id: \.self) { emoji in Button(emoji.isEmpty ? "Gỡ biểu cảm" : emoji) { Task { await store.react(message, emoji: emoji) } } }
                }
            }
            if !message.outgoing { Spacer(minLength: 45) }
        }
    }
}

struct SettingsView: View {
    @EnvironmentObject private var store: ChatStore
    @Environment(\.dismiss) private var dismiss
    @State private var confirm = false
    var body: some View {
        NavigationStack {
            Form {
                Section("Kết nối") { Text(store.pcName.isEmpty ? "Chưa ghép đôi" : store.pcName); Text(store.status).foregroundStyle(.secondary) }
                Section("Bản IPA cá nhân") {
                    Text("Nhận tin khi mở app. Chuyển nền hoặc khóa màn hình sẽ ngắt kết nối; mở lại để đồng bộ. Bản này không có thông báo đẩy APNs.")
                    Text("PC phải chạy Lantern và không ngủ. Tệp tối đa 100 MB. Tệp đang truyền cần chọn lại nếu kết nối bị ngắt.")
                    Text("Lịch sử được lưu mã hóa trên máy. Hội thoại có mật khẩu và tin tự hủy không được lưu vào cache bền vững.")
                }.font(.footnote)
                if store.paired { Section { Button("Quên PC và xóa cache", role: .destructive) { confirm = true } } }
            }
            .navigationTitle("Cài đặt").navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("Xong") { dismiss() } } }
            .confirmationDialog("Quên PC này?", isPresented: $confirm, titleVisibility: .visible) { Button("Quên PC", role: .destructive) { store.forget(); dismiss() } } message: { Text("Xóa ghép đôi, lịch sử cache và tin chờ trên iPhone. Dữ liệu PC được giữ. Muốn thu hồi thiết bị, dùng Gỡ thiết bị trên PC.") }
        }
    }
}

struct FilePreview: UIViewControllerRepresentable {
    let url: URL
    func makeCoordinator() -> Coordinator { Coordinator(url) }
    func makeUIViewController(context: Context) -> QLPreviewController { let view = QLPreviewController(); view.dataSource = context.coordinator; return view }
    func updateUIViewController(_ uiViewController: QLPreviewController, context: Context) {}
    final class Coordinator: NSObject, QLPreviewControllerDataSource {
        let url: URL; init(_ url: URL) { self.url = url }
        func numberOfPreviewItems(in controller: QLPreviewController) -> Int { 1 }
        func previewController(_ controller: QLPreviewController, previewItemAt index: Int) -> QLPreviewItem { url as NSURL }
    }
}
