#!/usr/bin/env python3
"""把人工把关后的 review.md 合并回评测集 → final.jsonl（剔除【不要用】）。"""
import json, os, re

D = os.path.expanduser("~/Downloads/voice-engine/ramc_eval")
md = open(f"{D}/review.md", encoding="utf-8").read()
orig = [json.loads(l) for l in open(f"{D}/mined.jsonl", encoding="utf-8")]

# 逐块解析：可选【不要用】 + **N.** 原话：… + 整理后：…
pat = re.compile(
    r"(?P<rej>【不要用】)?\*\*(?P<n>\d+)\.\*\*\s*原话：(?P<dirty>[^\n]+)\n+　　整理后：(?P<clean>[^\n]+)",
    re.M)
final, rejected = [], []
for m in pat.finditer(md):
    n = int(m.group("n"))
    if not (1 <= n <= len(orig)):
        continue
    base = orig[n - 1]
    if m.group("rej"):
        rejected.append(n); continue
    final.append({
        "id": n,
        "category": base["category"],
        "dirty": m.group("dirty").strip(),
        "clean": m.group("clean").strip(),
        "src": base["src"],
    })

with open(f"{D}/final.jsonl", "w", encoding="utf-8") as w:
    for c in final:
        w.write(json.dumps(c, ensure_ascii=False) + "\n")

print(f"✅ 有效 {len(final)} 条 / 剔除 {len(rejected)} 条 {rejected} → {D}/final.jsonl")
cats = {}
for c in final: cats[c["category"]] = cats.get(c["category"], 0) + 1
print("分布:", cats)
