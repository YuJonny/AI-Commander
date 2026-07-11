#!/usr/bin/env python3
"""从 MagicData-RAMC 逐字转写里挖真实中文口语脏话语，按现象分类采样。
输出: ~/Downloads/voice-engine/ramc_eval/mined.jsonl  {id, category, dirty, src}"""
import json, os, re, random, glob, collections

TXT = os.path.expanduser("~/Downloads/MagicData-RAMC 2/MDT2021S003/TXT")
OUT = os.path.expanduser("~/Downloads/voice-engine/ramc_eval")
os.makedirs(OUT, exist_ok=True)
random.seed(42)

FILLER = re.compile(r"[嗯呃]|(?:^|[，,])啊|啊(?:[，,]|$)")
# 连续重复：单字×3+ 或 2字词×2+（排除合法叠词常见的 AABB 太难判，用保守列表排除）
REP1 = re.compile(r"([一-鿿])\1{2,}")
REP2 = re.compile(r"([一-鿿]{2})\1")
LEGAL_REP = {"谢谢", "常常", "慢慢", "渐渐", "刚刚", "偷偷", "悄悄", "往往", "轻轻",
             "白白", "好好", "天天", "年年", "人人", "远远", "深深", "紧紧", "匆匆",
             "许多多"}
CORRECT = re.compile(r"不对|啊不是|说错|我是说|应该是说|不不不?是")
NUM = re.compile(r"[零一二三四五六七八九十百千万亿0-9]{3,}")
MARK = re.compile(r"\[[^\]]*\]")   # [+] [*] 等标注

def rep2_hits(t):
    return [m.group(0) for m in REP2.finditer(t) if m.group(1) not in LEGAL_REP]

def classify(t):
    cats = []
    if FILLER.search(t): cats.append("口水词")
    if REP1.search(t) or rep2_hits(t): cats.append("重复口吃")
    if CORRECT.search(t): cats.append("自我更正")
    if NUM.search(t): cats.append("含数字")
    if len(t) >= 40: cats.append("长句")
    return cats

pool = collections.defaultdict(list)
seen = set()
for f in glob.glob(f"{TXT}/*.txt"):
    for line in open(f, encoding="utf-8"):
        parts = line.rstrip("\n").split("\t")
        if len(parts) != 4: continue
        _, spk, meta, text = parts
        if spk == "G00000000" or "普通话" not in meta: continue
        if MARK.search(text): continue          # 带噪声/事件标注的跳过
        t = text.strip()
        if not (12 <= len(t) <= 70): continue
        key = re.sub(r"[^一-鿿]", "", t)[:20]   # 去重键
        if key in seen: continue
        cats = classify(t)
        if not cats: continue
        seen.add(key)
        pool[cats[0]].append({"dirty": t, "cats": cats, "src": os.path.basename(f)})

print("=== 现象池规模 ===")
for k, v in pool.items(): print(f"  {k}: {len(v)}")

QUOTA = {"自我更正": 12, "重复口吃": 16, "口水词": 16, "含数字": 8, "长句": 8}
picked = []
for cat, n in QUOTA.items():
    cand = pool.get(cat, [])
    random.shuffle(cand)
    picked += [{"id": f"{cat}-{i+1}", "category": cat, **c} for i, c in enumerate(cand[:n])]

with open(f"{OUT}/mined.jsonl", "w", encoding="utf-8") as w:
    for p in picked:
        w.write(json.dumps(p, ensure_ascii=False) + "\n")
print(f"\n✅ 采样 {len(picked)} 条 → {OUT}/mined.jsonl")
print("\n=== 每类样例 ===")
shown = set()
for p in picked:
    if p["category"] in shown: continue
    shown.add(p["category"])
    print(f"[{p['category']}] {p['dirty']}")
