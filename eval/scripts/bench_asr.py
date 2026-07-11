#!/usr/bin/env python3
"""中英混说 ASR A/B：SenseVoice vs Qwen3-ASR-0.6B vs FireRed2-CTC vs FireRed2-AED
用法: bench_asr.py <kind> <model_dir>   (子进程模式，输出 JSON)
     bench_asr.py all                   (驱动模式，跑全部并出表)
指标: 字符错误率 CER(标准化后) / 加载耗时 / 平均转写延迟 / 峰值内存"""
import json, os, re, resource, subprocess, sys, time, wave

BASE = os.path.expanduser("~/Downloads/voice-engine")
AB = f"{BASE}/asr-ab"
TESTSET = f"{AB}/testset"

MODELS = {
    "sensevoice":  f"{BASE}/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17",
    "qwen3-0.6b":  f"{AB}/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25",
    "firered-ctc": f"{AB}/sherpa-onnx-fire-red-asr2-ctc-zh_en-int8-2026-02-25",
    "firered-aed": f"{AB}/sherpa-onnx-fire-red-asr2-zh_en-int8-2026-02-26",
}

def norm(s):
    s = s.lower()
    s = re.sub(r"[^一-鿿a-z0-9]", "", s)
    return s

def cer(hyp, ref):
    h, r = norm(hyp), norm(ref)
    if not r: return 0.0
    dp = list(range(len(h) + 1))
    for i, rc in enumerate(r, 1):
        prev, dp[0] = dp[0], i
        for j, hc in enumerate(h, 1):
            cur = min(dp[j] + 1, dp[j-1] + 1, prev + (rc != hc))
            prev, dp[j] = dp[j], cur
    return dp[len(h)] / len(r)

def read_wav(p):
    import numpy as np
    w = wave.open(p, "rb")
    a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768.0
    return a, w.getframerate()

def load_recognizer(kind, d):
    import sherpa_onnx as s
    R = s.OfflineRecognizer
    def f(name):  # 找模型文件（优先 int8）
        for c in [f"{name}.int8.onnx", f"{name}.onnx"]:
            p = os.path.join(d, c)
            if os.path.exists(p): return p
        return ""
    tok = os.path.join(d, "tokens.txt")
    if kind == "sensevoice":
        return R.from_sense_voice(model=f("model"), tokens=tok, language="auto", use_itn=True)
    if kind == "qwen3-0.6b":
        return R.from_qwen3_asr(conv_frontend=f("conv_frontend"),
                                encoder=f("encoder"), decoder=f("decoder"),
                                tokenizer=os.path.join(d, "tokenizer"))
    if kind == "firered-ctc":
        return R.from_fire_red_asr_ctc(model=f("model"), tokens=tok)
    if kind == "firered-aed":
        return R.from_fire_red_asr(encoder=f("encoder"), decoder=f("decoder"), tokens=tok)
    raise ValueError(kind)

def bench_one(kind):
    d = MODELS[kind]
    truths = open(f"{TESTSET}/truth.txt").read().strip().split("\n")
    t0 = time.time()
    rec = load_recognizer(kind, d)
    load_s = time.time() - t0
    rows, lats = [], []
    for i, ref in enumerate(truths, 1):
        a, sr = read_wav(f"{TESTSET}/mix{i}.wav")
        t0 = time.time()
        st = rec.create_stream(); st.accept_waveform(sr, a); rec.decode_stream(st)
        lats.append(time.time() - t0)
        hyp = st.result.text.strip()
        rows.append({"ref": ref, "hyp": hyp, "cer": round(cer(hyp, ref), 4)})
    return {"kind": kind, "load_s": round(load_s, 2),
            "avg_lat": round(sum(lats)/len(lats), 3),
            "avg_cer": round(sum(r["cer"] for r in rows)/len(rows), 4),
            "maxrss_mb": round(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1048576),
            "rows": rows}

if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] != "all":
        print(json.dumps(bench_one(sys.argv[1]), ensure_ascii=False))
        sys.exit(0)
    # 驱动模式：每模型独立子进程（干净测内存）
    py = f"{BASE}/venv/bin/python"
    results = []
    for kind, d in MODELS.items():
        if not os.path.isdir(d):
            print(f"⏭ {kind}: 模型目录缺失，跳过"); continue
        r = subprocess.run([py, __file__, kind], capture_output=True, text=True, timeout=1800)
        line = r.stdout.strip().split("\n")[-1] if r.stdout.strip() else ""
        try:
            results.append(json.loads(line))
        except Exception:
            print(f"⚠️ {kind} 失败:\n{r.stderr[-800:]}")
    print(f"\n{'模型':<14}{'CER':>8}{'平均延迟':>10}{'加载':>8}{'峰值内存':>10}")
    print("-" * 52)
    for r in sorted(results, key=lambda x: x["avg_cer"]):
        print(f"{r['kind']:<14}{r['avg_cer']*100:>7.1f}%{r['avg_lat']:>9.2f}s{r['load_s']:>7.1f}s{r['maxrss_mb']:>8}MB")
    print("\n═══ 逐句对照 ═══")
    for i in range(len(results[0]["rows"]) if results else 0):
        print(f"\n[{i+1}] 真值: {results[0]['rows'][i]['ref']}")
        for r in results:
            row = r["rows"][i]
            print(f"  {r['kind']:<13} ({row['cer']*100:4.1f}%) {row['hyp']}")
