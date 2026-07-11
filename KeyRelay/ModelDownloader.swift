import Foundation

private func L(_ key: String) -> String {
    guard let lang = UserDefaults.standard.string(forKey: "appLanguage") else {
        return NSLocalizedString(key, comment: "")
    }
    guard let path = Bundle.main.path(forResource: lang, ofType: "lproj"),
          let bundle = Bundle(path: path) else {
        return NSLocalizedString(key, comment: "")
    }
    return bundle.localizedString(forKey: key, value: nil, table: nil)
}

/// 模型按需下载：解压/落位到 Application Support/AI接线员/models/。
/// 语音识别模型由用户显式选择下载：Qwen3 高精度档或 SenseVoice 轻量档；
/// LLM（Qwen3-8B GGUF ≈4.7GB）只在用户点击下载时落位。
/// 每项自带多个下载源（国内镜像优先），失败自动换源；校验通过才算成功。
final class ModelDownloader: NSObject {
    static let shared = ModelDownloader()

    struct Item {
        let canonicalDir: String   // models/ 下的目录名
        let urls: [String]         // 依次尝试的完整地址
        let requiredFiles: [String] // 就绪所需文件（相对 canonicalDir）
        let isArchive: Bool        // true=tar.bz2 解压；false=单文件直接落位
        let isGGUF: Bool           // GGUF 单文件走 Range 断点续传和 magic 校验
        let sizeHint: Double       // MB，用于合并进度

        var checkFile: String { requiredFiles[0] }
    }

    /// GitHub 直链 → 国内镜像优先的地址列表
    private static func gh(_ u: String) -> [String] {
        ["https://ghfast.top/" + u, "https://gh-proxy.com/" + u, u]
    }

    /// 高精度 ASR：Qwen3-0.6B（中英混说强、自带标点）
    static let asrQwen = Item(
        canonicalDir: "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25",
        urls: gh("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2"),
        requiredFiles: ["conv_frontend.onnx", "encoder.int8.onnx", "decoder.int8.onnx",
                        "tokenizer/tokenizer_config.json"],
        isArchive: true, isGGUF: false, sizeHint: 838)

    /// Qwen3 长语音分段：官方 Silero VAD（16kHz，约 0.63MB）。
    static let vad = Item(
        canonicalDir: "silero-vad",
        urls: gh("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx"),
        requiredFiles: ["silero_vad.onnx"],
        isArchive: false, isGGUF: false, sizeHint: 0.63)

    /// 轻量 ASR：SenseVoice（小而快、省内存）+ 配套标点模型
    static let asrSense = Item(
        canonicalDir: "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17",
        urls: gh("https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17.tar.bz2"),
        requiredFiles: ["model.int8.onnx", "tokens.txt"],
        isArchive: true, isGGUF: false, sizeHint: 155)

    static let punct = Item(
        canonicalDir: "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8",
        urls: gh("https://github.com/k2-fsa/sherpa-onnx/releases/download/punctuation-models/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8.tar.bz2"),
        requiredFiles: ["model.int8.onnx"],
        isArchive: true, isGGUF: false, sizeHint: 62)

    static let llm = Item(
        canonicalDir: "llm",
        urls: ["https://modelscope.cn/models/Qwen/Qwen3-8B-GGUF/resolve/master/Qwen3-8B-Q4_K_M.gguf",
               "https://hf-mirror.com/Qwen/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf",
               "https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf"],
        requiredFiles: ["Qwen3-8B-Q4_K_M.gguf"],
        isArchive: false, isGGUF: true, sizeHint: 4790)

    private(set) var isRunning = false
    var onProgress: ((String) -> Void)?
    var onFraction: ((Double) -> Void)?    // 0…1 总体进度（驱动进度条）
    var onFinished: ((Bool) -> Void)?

    static var modelsRoot: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("AI接线员/models", isDirectory: true)
    }

    static func isReady(_ item: Item) -> Bool {
        item.requiredFiles.allSatisfy { relativePath in
            let path = modelsRoot.appendingPathComponent(item.canonicalDir)
                .appendingPathComponent(relativePath).path
            var isDir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: path, isDirectory: &isDir) else { return false }
            if isDir.boolValue { return true }
            // 防零字节/截断残骸：坏模型文件喂进引擎可能在 C 层直接崩
            let size = ((try? FileManager.default.attributesOfItem(atPath: path)[.size]) as? NSNumber)?.int64Value ?? 0
            return size >= 1024
        }
    }

    /// Qwen3 需要 ASR + VAD；SenseVoice 档需要 ASR + 标点模型。
    static var anyASRReady: Bool {
        (isReady(asrQwen) && isReady(vad)) || (isReady(asrSense) && isReady(punct))
    }

    /// 下载 Qwen3（高精度）
    func startQwen() { run(items: [Self.asrQwen, Self.vad].filter { !Self.isReady($0) }) }
    /// 下载 SenseVoice（轻量）+ 标点模型
    func startSense() { run(items: [Self.asrSense, Self.punct].filter { !Self.isReady($0) }) }

    /// LLM 大模型：仅用户开"大模型增强"时调用
    func startLLM() { run(items: Self.isReady(Self.llm) ? [] : [Self.llm]) }

    private func run(items: [Item]) {
        guard !isRunning else { return }
        guard !items.isEmpty else { DispatchQueue.main.async { self.onFinished?(true) }; return }
        isRunning = true
        DispatchQueue.global(qos: .utility).async { [weak self] in
            guard let self else { return }
            var okAll = true
            let totalMB = items.reduce(0) { $0 + $1.sizeHint }
            var doneMB = 0.0
            for item in items {
                if self.fetch(item: item, doneMB: doneMB, totalMB: totalMB) {
                    doneMB += item.sizeHint
                } else { okAll = false; break }
            }
            self.isRunning = false
            let ok = okAll
            DispatchQueue.main.async {
                self.onProgress?(ok ? L("dl.done") : L("dl.fail"))
                self.onFinished?(ok)
            }
        }
    }

    private func fetch(item: Item, doneMB: Double, totalMB: Double) -> Bool {
        for urlStr in item.urls {
            guard let url = URL(string: urlStr) else { continue }
            if downloadAndInstall(url: url, item: item, doneMB: doneMB, totalMB: totalMB) { return true }
        }
        return false
    }

    private func downloadAndInstall(url: URL, item: Item, doneMB: Double, totalMB: Double) -> Bool {
        if item.isGGUF {
            // 大文件（GGUF）走断点续传
            return rangedDownload(url: url, item: item, doneMB: doneMB, totalMB: totalMB)
        }
        let sem = DispatchSemaphore(value: 0)
        var file: URL?
        let delegate = DownloadDelegate(onFraction: { [weak self] frac in
            let overall = (doneMB + frac * item.sizeHint) / totalMB
            DispatchQueue.main.async {
                self?.onFraction?(overall)
                self?.onProgress?(String(format: L("dl.progress.voice"), Int(overall * 100)))
            }
        }, completion: { loc in file = loc; sem.signal() })
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForRequest = 40
        cfg.timeoutIntervalForResource = 7200
        let session = URLSession(configuration: cfg, delegate: delegate, delegateQueue: nil)
        session.downloadTask(with: url).resume()
        sem.wait()
        session.finishTasksAndInvalidate()
        guard let f = file else { return false }
        defer { try? FileManager.default.removeItem(at: f) }
        return Self.install(downloaded: f, item: item)
    }

    /// 单大文件断点续传：写到 .part，失败保留下次接着下；完成校验 GGUF 头+体积后落位
    private func rangedDownload(url: URL, item: Item, doneMB: Double, totalMB: Double) -> Bool {
        let fm = FileManager.default
        let dir = Self.modelsRoot.appendingPathComponent(item.canonicalDir)
        try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        let dest = dir.appendingPathComponent(item.checkFile)
        let part = dir.appendingPathComponent(item.checkFile + ".part")
        if !fm.fileExists(atPath: part.path) { _ = fm.createFile(atPath: part.path, contents: nil) }
        let existing = ((try? fm.attributesOfItem(atPath: part.path)[.size]) as? NSNumber)?.int64Value ?? 0

        var req = URLRequest(url: url)
        if existing > 0 { req.setValue("bytes=\(existing)-", forHTTPHeaderField: "Range") }
        req.timeoutInterval = 40

        let hintBytes = item.sizeHint * 1_048_576
        let sem = DispatchSemaphore(value: 0)
        let delegate = RangedDelegate(partPath: part.path, existing: existing,
            onProgress: { [weak self] got, total in
                let t = total > 0 ? total : hintBytes
                let frac = min(1, Double(got) / t)
                DispatchQueue.main.async {
                    self?.onFraction?((doneMB + frac * item.sizeHint) / totalMB)
                    self?.onProgress?(String(format: L("dl.progress.llm"), Int(frac * 100)))
                }
            }, done: { sem.signal() })
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForRequest = 40
        cfg.timeoutIntervalForResource = 7200
        let session = URLSession(configuration: cfg, delegate: delegate, delegateQueue: nil)
        session.dataTask(with: req).resume()
        sem.wait()
        session.finishTasksAndInvalidate()
        guard delegate.ok else { return false }          // 失败：保留 .part 供续传

        // 校验：GGUF magic + 体积（防镜像塞 HTML 错误页）
        guard let fh = FileHandle(forReadingAtPath: part.path) else { return false }
        let magic = fh.readData(ofLength: 4); try? fh.close()
        let sz = ((try? fm.attributesOfItem(atPath: part.path)[.size]) as? NSNumber)?.doubleValue ?? 0
        guard String(data: magic, encoding: .ascii) == "GGUF", sz > hintBytes * 0.9 else {
            try? fm.removeItem(at: part)                 // 内容不对，重来
            return false
        }
        try? fm.removeItem(at: dest)
        do { try fm.moveItem(at: part, to: dest) } catch { return false }
        return Self.isReady(item)
    }

    /// 归档：解压→定位→挪到 canonicalDir；单文件：直接落位到 canonicalDir/checkFile
    static func install(downloaded: URL, item: Item) -> Bool {
        let fm = FileManager.default
        try? fm.createDirectory(at: modelsRoot, withIntermediateDirectories: true)

        if !item.isArchive {
            // 大小合理性校验（防镜像返回 HTML 错误页）
            let sz = (try? fm.attributesOfItem(atPath: downloaded.path)[.size] as? Int64) ?? 0
            guard Double(sz) > item.sizeHint * 1_000_000 * 0.9 else { return false }
            let dir = modelsRoot.appendingPathComponent(item.canonicalDir)
            try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
            let dest = dir.appendingPathComponent(item.checkFile)
            try? fm.removeItem(at: dest)
            do { try fm.moveItem(at: downloaded, to: dest) } catch { return false }
            return isReady(item)
        }

        let tmp = fm.temporaryDirectory.appendingPathComponent("aicmd-model-\(UUID().uuidString)")
        try? fm.createDirectory(at: tmp, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: tmp) }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/tar")
        p.arguments = ["xjf", downloaded.path, "-C", tmp.path]
        do { try p.run(); p.waitUntilExit() } catch { return false }
        guard p.terminationStatus == 0,
              let found = locate(checkFile: item.checkFile, under: tmp) else { return false }
        let dest = modelsRoot.appendingPathComponent(item.canonicalDir)
        try? fm.removeItem(at: dest)
        do { try fm.moveItem(at: found, to: dest) } catch { return false }
        return isReady(item)
    }

    private static func locate(checkFile: String, under dir: URL) -> URL? {
        let fm = FileManager.default
        if fm.fileExists(atPath: dir.appendingPathComponent(checkFile).path) { return dir }
        guard let kids = try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) else { return nil }
        for k in kids {
            var isDir: ObjCBool = false
            if fm.fileExists(atPath: k.path, isDirectory: &isDir), isDir.boolValue,
               fm.fileExists(atPath: k.appendingPathComponent(checkFile).path) { return k }
        }
        return nil
    }
}

/// Range 续传下载器：206=续写 .part；200=服务器不支持 Range，从头写；416=已完整
private final class RangedDelegate: NSObject, URLSessionDataDelegate {
    private let partPath: String
    private var received: Int64
    private var totalExpected: Double = 0
    private var fh: FileHandle?
    private let onProgress: (Int64, Double) -> Void
    private let done: () -> Void
    private var lastEmit = Date.distantPast
    private(set) var ok = false

    init(partPath: String, existing: Int64,
         onProgress: @escaping (Int64, Double) -> Void, done: @escaping () -> Void) {
        self.partPath = partPath
        self.received = existing
        self.onProgress = onProgress
        self.done = done
    }

    func urlSession(_ s: URLSession, dataTask t: URLSessionDataTask, didReceive response: URLResponse,
                    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard let http = response as? HTTPURLResponse else { completionHandler(.cancel); return }
        switch http.statusCode {
        case 206:   // 续传：Content-Range: bytes X-Y/Total
            if let cr = http.value(forHTTPHeaderField: "Content-Range"),
               let total = cr.split(separator: "/").last, let tv = Double(total) { totalExpected = tv }
            fh = FileHandle(forWritingAtPath: partPath)
            fh?.seekToEndOfFile()
            completionHandler(.allow)
        case 200:   // 不支持 Range：清空重下
            try? Data().write(to: URL(fileURLWithPath: partPath))
            received = 0
            totalExpected = Double(http.expectedContentLength)
            fh = FileHandle(forWritingAtPath: partPath)
            completionHandler(.allow)
        case 416:   // 请求范围越界 = .part 已是完整文件
            ok = true
            completionHandler(.cancel)
        default:
            completionHandler(.cancel)
        }
    }

    func urlSession(_ s: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        fh?.write(data)
        received += Int64(data.count)
        if Date().timeIntervalSince(lastEmit) > 0.3 {
            lastEmit = Date()
            onProgress(received, totalExpected)
        }
    }

    func urlSession(_ s: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        try? fh?.close()
        if error == nil { ok = true }          // 正常读完
        // error != nil 且非 416 取消 → ok 保持 false，.part 留给下次续传
        done()
    }
}

/// 进度 + 完成回调；校验 HTTP 200（镜像 404 也会"下载成功"一个错误页，必须挡掉）
private final class DownloadDelegate: NSObject, URLSessionDownloadDelegate {
    private let onFraction: (Double) -> Void
    private let completion: (URL?) -> Void
    private var lastEmit = Date.distantPast
    private var finished = false

    init(onFraction: @escaping (Double) -> Void, completion: @escaping (URL?) -> Void) {
        self.onFraction = onFraction
        self.completion = completion
    }

    func urlSession(_ s: URLSession, downloadTask t: URLSessionDownloadTask,
                    didWriteData _: Int64, totalBytesWritten w: Int64, totalBytesExpectedToWrite e: Int64) {
        guard e > 0, Date().timeIntervalSince(lastEmit) > 0.3 else { return }
        lastEmit = Date()
        onFraction(Double(w) / Double(e))
    }

    func urlSession(_ s: URLSession, downloadTask t: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        finished = true
        guard (t.response as? HTTPURLResponse)?.statusCode == 200 else { completion(nil); return }
        let dst = FileManager.default.temporaryDirectory.appendingPathComponent("aicmd-dl-\(UUID().uuidString).bin")
        do { try FileManager.default.moveItem(at: location, to: dst); completion(dst) }
        catch { completion(nil) }
    }

    func urlSession(_ s: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if error != nil, !finished { completion(nil) }
    }
}
