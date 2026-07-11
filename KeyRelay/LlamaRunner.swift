import Foundation
import AppKit

/// v2.1 内嵌 LLM 运行时：按需把打包的 llama-server 拉起为子进程，闲时退出。
/// 权重走 mmap（活动监视器里只显示几百 MB 私有内存），生命周期与 ASR 引擎同节奏。
final class LlamaRunner {
    static let shared = LlamaRunner()

    static let port: UInt16 = 43817
    static var baseURL: URL { URL(string: "http://127.0.0.1:\(port)")! }

    private var process: Process?
    private var idleStop: DispatchWorkItem?
    private let lock = NSLock()
    private let idleSeconds: TimeInterval = 300   // 5 分钟不用就退出，释放内存

    private init() {
        // App 退出时带走子进程
        NotificationCenter.default.addObserver(
            forName: NSApplication.willTerminateNotification, object: nil, queue: nil
        ) { [weak self] _ in self?.stop() }
    }

    /// GGUF 模型位置（Application Support/AI接线员/models/llm/）
    static var modelPath: String? {
        let p = ModelDownloader.modelsRoot.appendingPathComponent("llm/Qwen3-8B-Q4_K_M.gguf").path
        return FileManager.default.fileExists(atPath: p) ? p : nil
    }

    /// 内嵌 llama-server 位置：app bundle 优先，开发目录回退
    static var serverBinary: String? {
        if let fw = Bundle.main.privateFrameworksPath {
            let p = fw + "/llama/llama-server"
            if FileManager.default.fileExists(atPath: p) { return p }
        }
        let dev = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Downloads/接线员/vendor/llama.cpp/llama-b9873/llama-server")
        return FileManager.default.fileExists(atPath: dev.path) ? dev.path : nil
    }

    /// 确保 server 在跑（录音开始时调用 = 预热：进程一起来就开始 mmap 权重）
    func ensureRunning() {
        lock.lock(); defer { lock.unlock() }
        scheduleIdleStop()
        if let p = process, p.isRunning { return }
        if healthOK(timeout: 0.3) { return }   // 复用上次残留的 server（如上次 App 异常退出）
        guard let bin = Self.serverBinary, let model = Self.modelPath else { return }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: bin)
        p.arguments = ["-m", model,
                       "--host", "127.0.0.1",
                       "--port", String(Self.port),
                       "-ngl", "99",        // 全部层跑 Metal
                       "-c", "4096",
                       "--jinja",           // 启用聊天模板 → chat_template_kwargs 关思考
                       "--no-webui"]
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try? p.run()
        process = p
    }

    func stop() {
        lock.lock(); defer { lock.unlock() }
        idleStop?.cancel(); idleStop = nil
        process?.terminate()
        process = nil
    }

    private func scheduleIdleStop() {
        idleStop?.cancel()
        let w = DispatchWorkItem { [weak self] in self?.stop() }
        idleStop = w
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + idleSeconds, execute: w)
    }

    /// 等 server 就绪（加载 4.7GB 权重需数秒；通常已被说话间隙覆盖）
    func waitReady(seconds: Double) -> Bool {
        let deadline = Date().addingTimeInterval(seconds)
        while Date() < deadline {
            if healthOK(timeout: 1.0) { return true }
            usleep(300_000)
        }
        return false
    }

    private func healthOK(timeout: TimeInterval) -> Bool {
        var req = URLRequest(url: Self.baseURL.appendingPathComponent("health"))
        req.timeoutInterval = timeout
        let sem = DispatchSemaphore(value: 0)
        var ok = false
        URLSession.shared.dataTask(with: req) { _, r, _ in
            ok = (r as? HTTPURLResponse)?.statusCode == 200
            sem.signal()
        }.resume()
        sem.wait()
        return ok
    }
}
