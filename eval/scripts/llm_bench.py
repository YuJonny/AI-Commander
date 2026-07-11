#!/usr/bin/env python3
"""v2.1 选型评测：qwen2.5:7b vs qwen3:4b vs qwen3:8b
同一套语音整理任务：口误更正 / 去重 / 数字红线 / 防省略 / 结构化 + 热延迟。"""
import json, re, sys, time, urllib.request

HOST = "http://localhost:11434"

PROMPT = """你是中文语音转写整理助手。把口述整理成通顺的书面文字。
只允许做这些：去掉口水词（嗯、呃、这个、那个）；合并明显的重复；执行说话人的自我更正（"啊不对/不是"以最后说的为准）；补标点。
铁律：
- 这不是摘要任务。禁止省略、压缩、改写任何信息点：地址、地名、人名、数字、时间、单位名，一个都不能丢。
- 听不通顺、疑似识别错误的片段，原样保留在原位置，禁止删除、禁止猜测性改写。
- 不得新增信息，不得编造数字。
- 输出的信息量必须与口述完全一致，只是更干净。
只输出整理后的文字，不要解释。

口述：
"""

# (名字, 输入, [必须包含], [必须不含])
CASES = [
    ("口误更正", "嗯那个我想说的是呃明天三点啊不对两点半开会然后把方案发给老王不对发给老李",
     ["两点半", "老李"], ["三点", "老王", "嗯", "呃"]),
    ("数字红线", "订单号是三八七二五九金额一万二千三百四十五块六毛",
     [], []),   # 人工看：数字不许变
    ("防省略(用户真实案例)", "将会议时间定为两点三十分我手机号是13506823776我家住在北京市朝阳区那个廷公寓2ED单元门门口的",
     ["13506823776", "北京市朝阳区", "廷公寓"], []),
    ("去重复", "这个这个方案我觉得我觉得还是能改一改",
     ["方案"], []),
    ("结构化", "这周主要干了三件事吧首先是把语音输入做完了然后呢就是修了那个内存占用太高的问题对了还有就是把下载模型的功能也搞定了大概就这些",
     ["语音输入", "内存", "下载"], []),
]

def call(model, text, think_off):
    body = {"model": model, "prompt": PROMPT + text, "stream": False,
            "options": {"temperature": 0}, "keep_alive": "3m"}
    if think_off:
        body["think"] = False
    payload = json.dumps(body).encode()
    req = urllib.request.Request(f"{HOST}/api/generate", data=payload,
                                 headers={"Content-Type": "application/json"})
    t0 = time.time()
    out = json.loads(urllib.request.urlopen(req, timeout=300).read())["response"]
    dt = time.time() - t0
    out = re.sub(r"<think>.*?</think>", "", out, flags=re.S).strip()
    return out, dt

def main():
    models = sys.argv[1:] or ["qwen2.5:7b", "qwen3:4b", "qwen3:8b"]
    for m in models:
        think_off = m.startswith("qwen3")  # qwen3/3.5 原生参数关思考
        print(f"\n{'='*56}\n▍{m}\n{'='*56}")
        try:
            call(m, "预热", think_off)   # 载入，不计时
        except Exception as e:
            print(f"  ⚠️ 无法调用: {e}"); continue
        lat = []
        for name, text, must, mustnot in CASES:
            try:
                out, dt = call(m, text, think_off)
            except Exception as e:
                print(f"[{name}] ⚠️ {e}"); continue
            lat.append(dt)
            miss = [w for w in must if w not in out]
            bad  = [w for w in mustnot if w in out]
            flag = "✅" if not miss and not bad else "❌"
            print(f"\n[{name}] {flag}  {dt:.1f}s")
            print(f"  出: {out[:160]}")
            if miss: print(f"  ✗ 丢了: {miss}")
            if bad:  print(f"  ✗ 该删没删/该改没改: {bad}")
        if lat:
            print(f"\n  ⏱ 热延迟: 平均 {sum(lat)/len(lat):.1f}s  最慢 {max(lat):.1f}s")

if __name__ == "__main__":
    main()
