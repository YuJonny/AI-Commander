#!/usr/bin/env python3
"""VAD 参数 A/B：复现 App 的 VAD→逐段 Qwen3 ASR→拼接，对比不同 threshold/min_silence。
用法: vad_ab.py <input.wav>
"""
import os, sys, time, wave
import numpy as np
import sherpa_onnx as s

WAV = sys.argv[1]
MODELS = os.path.expanduser("~/Library/Application Support/AI接线员/models")
QWEN = f"{MODELS}/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25"
VAD_MODEL = f"{MODELS}/silero-vad/silero_vad.onnx"

def read_wav(p):
    w = wave.open(p, "rb")
    assert w.getframerate() == 16000 and w.getnchannels() == 1
    a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768.0
    return a

def pick(name):
    for c in (f"{QWEN}/{name}.int8.onnx", f"{QWEN}/{name}.onnx"):
        if os.path.exists(c):
            return c
    raise FileNotFoundError(name)

def load_qwen():
    return s.OfflineRecognizer.from_qwen3_asr(
        conv_frontend=pick("conv_frontend"),
        encoder=pick("encoder"),
        decoder=pick("decoder"),
        tokenizer=f"{QWEN}/tokenizer",
    )

def vad_segments(a, threshold, min_sil, max_speech=20.0):
    cfg = s.VadModelConfig(
        silero_vad=s.SileroVadModelConfig(
            model=VAD_MODEL,
            threshold=threshold,
            min_silence_duration=min_sil,
            min_speech_duration=0.25,
            max_speech_duration=max_speech,
            window_size=512,
        ),
        sample_rate=16000,
    )
    vad = s.VoiceActivityDetector(cfg, buffer_size_in_seconds=120)
    segs = []
    i = 0
    while i < len(a):
        vad.accept_waveform(a[i:i+512])
        i += 512
        while not vad.empty():
            segs.append(np.array(vad.front.samples, dtype=np.float32))
            vad.pop()
    try:
        vad.flush()
    except AttributeError:
        pass
    while not vad.empty():
        segs.append(np.array(vad.front.samples, dtype=np.float32))
        vad.pop()
    return segs

def decode(rec, seg):
    st = rec.create_stream()
    st.accept_waveform(16000, seg)
    rec.decode_stream(st)
    return st.result.text.strip()

def join(parts):
    # 复刻 Swift joinSegments：仅前后都是 ASCII 字母数字时补空格
    out = ""
    for p in parts:
        if not p:
            continue
        if out and out[-1].isascii() and out[-1].isalnum() and p[0].isascii() and p[0].isalnum():
            out += " "
        out += p
    return out

def main():
    a = read_wav(WAV)
    dur = len(a) / 16000
    print(f"音频 {dur:.1f}s · {WAV}")
    rec = load_qwen()
    grid = [
        (0.20, 0.5, "现状"),
        (0.20, 0.8, ""),
        (0.20, 1.0, ""),
        (0.20, 1.5, ""),
        (0.20, 2.0, ""),
        (0.50, 1.0, ""),
    ]
    for thr, ms, tag in grid:
        segs = vad_segments(a, thr, ms)
        t0 = time.time()
        parts = [decode(rec, sg) for sg in segs]
        cost = time.time() - t0
        joined = join(parts)
        mid_periods = joined[:-1].count("。")  # 非末尾句号 = 句中断句代理指标
        durs = " ".join(f"{len(sg)/16000:.1f}" for sg in segs)
        print(f"\n== thr={thr} minSil={ms}s {tag} | 段数={len(segs)} 解码={cost:.1f}s 句中句号={mid_periods}")
        print(f"   段长: {durs}")
        for i, p in enumerate(parts, 1):
            print(f"   [{i}] {p}")
        print(f"   拼接: {joined}")

if __name__ == "__main__":
    main()
