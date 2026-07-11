import Foundation
import AVFoundation

/// 麦克风采音：录制时按原生格式累积单声道样本，停止时**整段一次性**重采样到
/// 16k 单声道（SenseVoice 要求）。整段转换比逐块转换可靠得多。
final class AudioRecorder {
    struct Capture {
        let samples: [Float]
        let sampleRate: Int
        let nativeSampleRate: Double
        let nativeSampleCount: Int
        let channelCount: Int
    }

    private let engine = AVAudioEngine()
    private let sampleLock = NSLock()
    // 以下状态跨线程读（主线程写、语音队列读），统一在 sampleLock 下访问
    private var nativeSR: Double = 48000
    private var nativeChannels: Int = 1
    private var acc: [Float] = []
    private var recording = false

    var isRecording: Bool {
        sampleLock.lock()
        defer { sampleLock.unlock() }
        return recording
    }

    /// 当前采音的原生采样率（供伪流式重采样器初始化）
    var nativeRate: Double {
        sampleLock.lock()
        defer { sampleLock.unlock() }
        return nativeSR
    }

    /// 伪流式增量读取：返回 offset 之后新累积的原生样本副本。
    /// 只读不消费，主缓冲不受影响（停止时的整段重采样/诊断 WAV 照常）。
    func nativeSamples(after offset: Int) -> [Float] {
        sampleLock.lock()
        defer { sampleLock.unlock() }
        guard offset >= 0, offset < acc.count else { return [] }
        return Array(acc[offset...])
    }

    func start() throws {
        let input = engine.inputNode
        let fmt = input.outputFormat(forBus: 0)     // 麦克风原生格式
        sampleLock.lock()
        acc.removeAll(keepingCapacity: true)
        nativeSR = fmt.sampleRate
        nativeChannels = Int(fmt.channelCount)
        sampleLock.unlock()
        input.installTap(onBus: 0, bufferSize: 4096, format: fmt) { [weak self] buf, _ in
            guard let self, let ch = buf.floatChannelData else { return }
            let n = Int(buf.frameLength)
            // 取 0 声道（内建麦克风通常单声道）；当场拷贝出来
            let chunk = Array(UnsafeBufferPointer(start: ch[0], count: n))
            self.sampleLock.lock()
            self.acc.append(contentsOf: chunk)
            self.sampleLock.unlock()
        }
        engine.prepare()
        try engine.start()
        sampleLock.lock()
        recording = true
        sampleLock.unlock()
    }

    func stop() -> Capture {
        sampleLock.lock()
        let wasRecording = recording
        recording = false
        sampleLock.unlock()
        if wasRecording {
            engine.inputNode.removeTap(onBus: 0)
            engine.stop()
        }
        sampleLock.lock()
        let captured = acc
        let sr = nativeSR
        let channels = nativeChannels
        sampleLock.unlock()
        return Capture(
            samples: AudioRecorder.resampleTo16k(captured, from: sr),
            sampleRate: 16_000,
            nativeSampleRate: sr,
            nativeSampleCount: captured.count,
            channelCount: channels)
    }

    /// 整段重采样到 16k 单声道（一次性喂入 + endOfStream 排空，结果稳定）
    static func resampleTo16k(_ input: [Float], from sr: Double) -> [Float] {
        if input.isEmpty { return [] }
        if abs(sr - 16000) < 1 { return input }
        guard
            let inFmt = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: sr, channels: 1, interleaved: false),
            let outFmt = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false),
            let conv = AVAudioConverter(from: inFmt, to: outFmt),
            let inBuf = AVAudioPCMBuffer(pcmFormat: inFmt, frameCapacity: AVAudioFrameCount(input.count))
        else {
            // 返回原采样率样本会让 ASR 吃错采样率吐乱码；宁可空返回走"没听到声音"
            NSLog("[AudioRecorder] resample setup failed (sr=\(sr))，丢弃本次音频")
            return []
        }

        inBuf.frameLength = AVAudioFrameCount(input.count)
        input.withUnsafeBufferPointer { p in
            inBuf.floatChannelData![0].update(from: p.baseAddress!, count: input.count)
        }
        let outCap = AVAudioFrameCount(Double(input.count) * 16000.0 / sr + 2048)
        guard let outBuf = AVAudioPCMBuffer(pcmFormat: outFmt, frameCapacity: outCap) else {
            NSLog("[AudioRecorder] resample buffer alloc failed，丢弃本次音频")
            return []
        }

        var done = false
        var err: NSError?
        conv.convert(to: outBuf, error: &err) { _, status in
            if done { status.pointee = .endOfStream; return nil }
            done = true
            status.pointee = .haveData
            return inBuf
        }
        if let err { NSLog("[AudioRecorder] resample error: \(err)") }
        let ch = outBuf.floatChannelData![0]
        return Array(UnsafeBufferPointer(start: ch, count: Int(outBuf.frameLength)))
    }
}

/// 伪流式重采样器：**持久化**的 AVAudioConverter 顺序喂块（正确的流式用法——
/// 早年"逐块转换不可靠"是因为每块新建转换器丢内部状态）。
/// 仅供伪流式 VAD/逐段 ASR 使用；诊断 WAV 与批式回退仍走整段一次性重采样。
final class StreamingResampler {
    private let converter: AVAudioConverter?
    private let inFmt: AVAudioFormat?
    private let outFmt: AVAudioFormat?
    private let passthrough: Bool
    private let ratio: Double

    init(from sr: Double) {
        if abs(sr - 16_000) < 1 {
            passthrough = true
            converter = nil; inFmt = nil; outFmt = nil; ratio = 1
            return
        }
        passthrough = false
        ratio = 16_000.0 / sr
        let i = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: sr, channels: 1, interleaved: false)
        let o = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16_000, channels: 1, interleaved: false)
        inFmt = i; outFmt = o
        converter = (i != nil && o != nil) ? AVAudioConverter(from: i!, to: o!) : nil
    }

    /// 喂一块原生样本，返回本次产出的 16k 样本（converter 内部可能留少量尾巴，后续 feed/flush 吐出）
    func feed(_ input: [Float]) -> [Float] {
        if passthrough { return input }
        guard !input.isEmpty, let conv = converter, let inFmt, let outFmt,
              let inBuf = AVAudioPCMBuffer(pcmFormat: inFmt, frameCapacity: AVAudioFrameCount(input.count))
        else { return [] }
        inBuf.frameLength = AVAudioFrameCount(input.count)
        input.withUnsafeBufferPointer { p in
            inBuf.floatChannelData![0].update(from: p.baseAddress!, count: input.count)
        }
        let outCap = AVAudioFrameCount(Double(input.count) * ratio) + 256
        guard let outBuf = AVAudioPCMBuffer(pcmFormat: outFmt, frameCapacity: outCap) else { return [] }
        var supplied = false
        conv.convert(to: outBuf, error: nil) { _, status in
            if supplied { status.pointee = .noDataNow; return nil }   // 流式续块：不是 endOfStream
            supplied = true
            status.pointee = .haveData
            return inBuf
        }
        let ch = outBuf.floatChannelData![0]
        return Array(UnsafeBufferPointer(start: ch, count: Int(outBuf.frameLength)))
    }

    /// 结束：排空 converter 内部残留
    func flush() -> [Float] {
        if passthrough { return [] }
        guard let conv = converter, let outFmt,
              let outBuf = AVAudioPCMBuffer(pcmFormat: outFmt, frameCapacity: 4096) else { return [] }
        conv.convert(to: outBuf, error: nil) { _, status in
            status.pointee = .endOfStream
            return nil
        }
        let ch = outBuf.floatChannelData![0]
        return Array(UnsafeBufferPointer(start: ch, count: Int(outBuf.frameLength)))
    }
}
