#!/usr/bin/env python3
"""规则版 vs 大模型增强版 —— 清理准确性对照实验。
两条管线均复刻 App 真实路径：
  规则版   = rules.clean → 剥标点 → 标点模型 → 去重
  增强版   = rules.clean → 内嵌 llama-server(Qwen3-8B, 关思考, 铁律prompt)
指标：信息保真率(must 保留) / 清理到位率(must_not 消除) / 延迟。
"""
import json, re, subprocess, sys, time, urllib.request

sys.path.insert(0, "/Users/vvclaw/Downloads/voice-engine")
import rules as R
import sherpa_onnx

# ---------- 标点模型（同 App polish 路径） ----------
PUNC = "/Users/vvclaw/Downloads/voice-engine/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8/model.int8.onnx"
pc = sherpa_onnx.OfflinePunctuationConfig(model=sherpa_onnx.OfflinePunctuationModelConfig(ct_transformer=PUNC, num_threads=1))
PUNCT = sherpa_onnx.OfflinePunctuation(pc)

def strip_punct(s): return re.sub(r"[，。！？；、：,;!?]", "", s)
def dedupe(s):
    s = re.sub(r"[，。！？；、：]+([，。！？；、：])", r"\1", s)
    return re.sub(r"([,;!?])[,;!?]+", r"\1", s)

def pipe_rules(text):
    t = R.clean(text)
    return dedupe(PUNCT.add_punctuation(strip_punct(t)))

# ---------- LLM（App 同款 prompt / 参数） ----------
PROMPT = """你是中文语音转写整理助手。把口述整理成通顺的书面文字。
只允许做这些：去掉口水词（嗯、呃、这个、那个）；合并明显的重复；执行说话人的自我更正（"啊不对/不是"以最后说的为准）；补标点。
铁律：
- 自我更正必须执行：被"啊不对/不是"否定的旧说法（时间、人名、数字等）必须删掉，只保留更正后的——这不算省略。
- 除上述更正外，这不是摘要任务。禁止省略、压缩、改写任何信息点：地址、地名、人名、数字、时间、单位名，一个都不能丢。
- 听不通顺、疑似识别错误的片段，原样保留在原位置，禁止删除、禁止猜测性改写。
- 不得新增信息，不得编造数字。
- 输出的信息量必须与口述完全一致（更正掉的旧说法除外），只是更干净。
只输出整理后的文字，不要解释。

口述：
"""

def pipe_llm(text):
    pre = R.clean(text)   # App: 规则先删口水词
    body = {"messages": [{"role": "user", "content": PROMPT + pre}],
            "temperature": 0, "chat_template_kwargs": {"enable_thinking": False}}
    req = urllib.request.Request("http://127.0.0.1:43817/v1/chat/completions",
                                 data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"})
    out = json.loads(urllib.request.urlopen(req, timeout=120).read())
    t = out["choices"][0]["message"]["content"]
    return re.sub(r"<think>[\s\S]*?</think>", "", t).strip()

# ---------- 测试集：ASR 风格脏输入 ----------
# (类别, 输入, must必须保留, must_not必须消除)
CASES = [
    ("口水词", "嗯那个我想说的是呃这个方案挺好的",
     ["方案挺好"], ["嗯", "呃"]),
    ("口水词", "啊就是我们明天上午十点碰一下吧",
     ["明天上午十点"], []),
    ("口误更正", "会议改到周三啊不对周四下午",
     ["周四"], ["周三"]),
    ("口误更正", "把文件发给小张不是发给小刘",
     ["小刘"], ["小张"]),
    ("口误更正", "预算是五万啊不对是八万",
     ["八万"], ["五万"]),
    ("重复", "我我我觉得这个这个功能还挺实用的",
     ["功能", "实用"], ["我我", "这个这个"]),
    ("重复", "就是说就是说今天先到这里",
     ["今天先到这里"], ["就是说就是说"]),
    ("信息保真", "手机号13506823776地址北京市朝阳区望京西园四区518号楼",
     ["13506823776", "北京市朝阳区", "望京西园四区", "518"], []),
    ("信息保真", "订单号三八七二五九金额12345块6",
     ["12345"], []),
    ("长句断句", "今天开了三个会第一个聊预算第二个聊排期第三个聊人员安排晚上还要写周报",
     ["预算", "排期", "人员安排", "周报"], []),
    ("混合", "嗯那个客户说呃合同金额是二十万啊不对是三十五万然后然后下周一签",
     ["三十五万", "下周一"], ["二十万", "嗯", "呃"]),
    ("混合", "这个报告我我今天写不完了明天呃明天上午一定给你",
     ["报告", "明天上午"], ["我我"]),
]

def evaluate(name, fn):
    keep_ok = keep_all = fix_ok = fix_all = 0
    lat = []
    rows = []
    for cat, text, must, mustnot in CASES:
        t0 = time.time()
        try:
            out = fn(text)
        except Exception as e:
            rows.append((cat, text, f"⚠️ {e}", [], []))
            keep_all += len(must); fix_all += len(mustnot)
            continue
        lat.append(time.time() - t0)
        miss = [w for w in must if w not in out]
        bad = [w for w in mustnot if w in out]
        keep_ok += len(must) - len(miss); keep_all += len(must)
        fix_ok += len(mustnot) - len(bad); fix_all += len(mustnot)
        rows.append((cat, text, out, miss, bad))
    print(f"\n{'='*62}\n▍{name}\n{'='*62}")
    for cat, text, out, miss, bad in rows:
        flag = "✅" if not miss and not bad else "❌"
        print(f"[{cat}] {flag}")
        print(f"  入: {text}")
        print(f"  出: {out}")
        if miss: print(f"  ✗ 丢失: {miss}")
        if bad:  print(f"  ✗ 未处理: {bad}")
    kr = keep_ok / keep_all * 100
    fr = fix_ok / fix_all * 100
    print(f"\n  📊 信息保真率: {keep_ok}/{keep_all} = {kr:.0f}%")
    print(f"  📊 清理到位率: {fix_ok}/{fix_all} = {fr:.0f}%")
    if lat: print(f"  ⏱ 平均延迟: {sum(lat)/len(lat):.2f}s")
    return kr, fr

if __name__ == "__main__":
    r = evaluate("规则版（规则+标点模型）", pipe_rules)
    l = evaluate("增强版（规则+Qwen3-8B）", pipe_llm)
    print(f"\n{'='*62}\n▍总结\n{'='*62}")
    print(f"信息保真率:  规则 {r[0]:.0f}%  vs  增强 {l[0]:.0f}%")
    print(f"清理到位率:  规则 {r[1]:.0f}%  vs  增强 {l[1]:.0f}%")
