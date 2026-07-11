#!/usr/bin/env python3
"""Qwen3-8B vs Qwen3-14B 本地润色 A/B：真实诊断口述 + 产品草稿 + RAMC 回归。"""

import argparse
import difflib
import hashlib
import html
import json
import os
import re
import statistics
import subprocess
import time
import urllib.error
import urllib.request
from pathlib import Path


EVAL_DIR = Path(__file__).resolve().parent
PROJECT_DIR = EVAL_DIR.parent
OUTPUT_DIR = EVAL_DIR / "llm_ab"
DEFAULT_DIAGNOSTICS = Path.home() / "Library/Application Support/AI接线员/diagnostics"
DEFAULT_8B = Path.home() / "Library/Application Support/AI接线员/models/llm/Qwen3-8B-Q4_K_M.gguf"
EXTERNAL_MODEL_BUNDLE = Path("/Volumes/UNTITLED/mac mini extra/AIReceptionistModels.sparsebundle")
EXTERNAL_MODEL_MOUNT = Path("/Volumes/AIReceptionistModels")
DEFAULT_14B = EXTERNAL_MODEL_MOUNT / "AI接线员/models/llm-ab/Qwen3-14B-Q4_K_M.gguf"
DEFAULT_SERVER = PROJECT_DIR / "vendor/llama.cpp/llama-b9873/llama-server"
PROMPT_PATH = EVAL_DIR / "prompts/prompt_v1.txt"
PORT = 43829


def read_jsonl(path):
    if not path.exists():
        return []
    with path.open(encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def stage_text(trace, preferred_names):
    stages = trace.get("stages", [])
    for name in preferred_names:
        values = [stage.get("text") for stage in stages if stage.get("name") == name and stage.get("text")]
        if values:
            return values[-1], name
    return None, None


def collect_cases(diagnostics_root, include_ramc=True):
    drafts = read_jsonl(EVAL_DIR / "product_scenarios.draft.jsonl")
    cases = []
    matched_drafts = set()

    if diagnostics_root.exists():
        trace_paths = sorted(diagnostics_root.glob("*/trace.json"), reverse=True)
        for trace_path in trace_paths:
            with trace_path.open(encoding="utf-8") as handle:
                trace = json.load(handle)
            text, input_stage = stage_text(
                trace,
                ["llm.input", "rules.completed", "terms.normalized", "asr.joined_raw"],
            )
            if not text:
                continue
            session_id = trace.get("sessionID", trace_path.parent.name)
            draft = None
            for item in drafts:
                marker = item.get("src", "").replace("trace ", "").strip()
                if marker and marker in session_id:
                    draft = item
                    matched_drafts.add(item["id"])
                    break
            cases.append({
                "id": f"diag-{session_id}",
                "group": "真实口述（优先 Review）",
                "category": draft.get("category") if draft else "真实诊断会话",
                "source": f"diagnostics/{session_id} · {input_stage}",
                "started_at": trace.get("startedAt"),
                "dirty": text,
                "clean": draft.get("clean") if draft else None,
                "must_keep": draft.get("must_keep", []) if draft else [],
                "must_not_include": draft.get("must_not_include", []) if draft else [],
                "gold_status": "待用户确认的产品金标" if draft else "无金标，人工并排 Review",
            })

    for draft in drafts:
        if draft["id"] in matched_drafts:
            continue
        cases.append({
            "id": f"product-{draft['id']}",
            "group": "产品场景草稿",
            "category": draft.get("category", "产品场景"),
            "source": draft.get("src", "product_scenarios.draft.jsonl"),
            "dirty": draft["dirty"],
            "clean": draft.get("clean"),
            "must_keep": draft.get("must_keep", []),
            "must_not_include": draft.get("must_not_include", []),
            "gold_status": "待用户确认的产品金标",
        })

    if include_ramc:
        for item in read_jsonl(EVAL_DIR / "final.jsonl"):
            cases.append({
                "id": f"ramc-{item['id']}",
                "group": "RAMC 56 条回归",
                "category": item.get("category", "RAMC"),
                "source": item.get("src", "eval/final.jsonl"),
                "dirty": item["dirty"],
                "clean": item.get("clean"),
                "must_keep": item.get("must_keep", []),
                "must_not_include": item.get("must_not_include", []),
                "gold_status": "人工金标准",
            })
    return cases


def format_prompt(template, text):
    return template % text if "%s" in template else f"{template.rstrip()}\n\n<口述>{text}</口述>"


class LocalServer:
    def __init__(self, binary, model, label, output_dir, port=PORT):
        self.binary = binary
        self.model = model
        self.label = label
        self.output_dir = output_dir
        self.port = port
        self.process = None
        self.log_handle = None
        self.load_seconds = None

    @property
    def endpoint(self):
        return f"http://127.0.0.1:{self.port}"

    def start(self):
        if not self.binary.exists():
            raise FileNotFoundError(f"llama-server 不存在：{self.binary}")
        if not self.model.exists():
            raise FileNotFoundError(f"模型不存在：{self.model}")
        self.output_dir.mkdir(parents=True, exist_ok=True)
        self.log_handle = (self.output_dir / f"server-{self.label}.log").open("w", encoding="utf-8")
        environment = os.environ.copy()
        library_dir = str(self.binary.parent)
        environment["DYLD_LIBRARY_PATH"] = library_dir + (
            ":" + environment["DYLD_LIBRARY_PATH"] if environment.get("DYLD_LIBRARY_PATH") else ""
        )
        command = [
            str(self.binary), "-m", str(self.model),
            "--host", "127.0.0.1", "--port", str(self.port),
            "-ngl", "99", "-c", "4096", "--jinja", "--no-webui",
        ]
        started = time.perf_counter()
        self.process = subprocess.Popen(
            command,
            cwd=self.binary.parent,
            env=environment,
            stdout=self.log_handle,
            stderr=subprocess.STDOUT,
        )
        deadline = time.time() + 240
        while time.time() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError(f"{self.label} llama-server 提前退出，见 {self.log_handle.name}")
            try:
                with urllib.request.urlopen(self.endpoint + "/health", timeout=2) as response:
                    if response.status == 200:
                        self.load_seconds = time.perf_counter() - started
                        return
            except Exception:
                time.sleep(0.5)
        raise TimeoutError(f"{self.label} 模型 240 秒内未就绪")

    def stop(self):
        if self.process and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=10)
        if self.log_handle:
            self.log_handle.close()
        self.process = None
        self.log_handle = None


def call_model(endpoint, prompt, timeout=180):
    body = {
        "messages": [{"role": "user", "content": prompt}],
        "temperature": 0,
        "chat_template_kwargs": {"enable_thinking": False},
    }
    request = urllib.request.Request(
        endpoint + "/v1/chat/completions",
        data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
        headers={"Content-Type": "application/json"},
    )
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            payload = json.loads(response.read())
        raw = payload["choices"][0]["message"]["content"]
        output = re.sub(r"<think>[\s\S]*?</think>", "", raw)
        output = re.sub(r"[ \t]+\n", "\n", output).strip()
        return {
            "output": output,
            "raw_output": raw,
            "latency_seconds": time.perf_counter() - started,
            "usage": payload.get("usage", {}),
            "error": None,
        }
    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, KeyError, json.JSONDecodeError) as error:
        return {
            "output": "",
            "raw_output": "",
            "latency_seconds": time.perf_counter() - started,
            "usage": {},
            "error": str(error),
        }


def edit_distance(left, right):
    if not left:
        return len(right)
    previous = list(range(len(right) + 1))
    for index, char_left in enumerate(left, 1):
        current = [index]
        for other_index, char_right in enumerate(right, 1):
            current.append(min(
                current[-1] + 1,
                previous[other_index] + 1,
                previous[other_index - 1] + (char_left != char_right),
            ))
        previous = current
    return previous[-1]


def normalize(text):
    return re.sub(r"\s+", "", re.sub(r"[，。！？、；：,.!?;:\n]", "", text or ""))


def similarity(output, gold):
    output_norm, gold_norm = normalize(output), normalize(gold)
    if not gold_norm:
        return 1.0
    return 1 - edit_distance(output_norm, gold_norm) / max(len(output_norm), len(gold_norm), 1)


def compact(text):
    return re.sub(r"\s+", "", text or "").lower()


def contains(output, needle):
    return needle in output or compact(needle) in compact(output)


def score_output(case, output):
    gold = case.get("clean")
    must_keep = case.get("must_keep", [])
    must_not = case.get("must_not_include", [])
    missing = [item for item in must_keep if not contains(output, item)]
    forbidden = [item for item in must_not if contains(output, item)]
    wanted_digits = re.findall(r"[0-9]+", gold or case["dirty"])
    return {
        "similarity": similarity(output, gold) if gold else None,
        "missing_must_keep": missing,
        "forbidden_present": forbidden,
        "redline_ok": not missing and not forbidden if must_keep or must_not else None,
        "digit_ok": all(number in output for number in wanted_digits) if wanted_digits else None,
        "input_character_delta": len(output) - len(case["dirty"]),
    }


def comparison_winner(score_8b, score_14b):
    if score_8b["redline_ok"] is not None and score_8b["redline_ok"] != score_14b["redline_ok"]:
        return "8B" if score_8b["redline_ok"] else "14B"
    if score_8b["digit_ok"] is not None and score_8b["digit_ok"] != score_14b["digit_ok"]:
        return "8B" if score_8b["digit_ok"] else "14B"
    left, right = score_8b["similarity"], score_14b["similarity"]
    if left is not None and right is not None and abs(left - right) >= 0.005:
        return "8B" if left > right else "14B"
    return "自动指标持平/无金标"


def fragments(left, right):
    left_only, right_only = [], []
    for tag, i1, i2, j1, j2 in difflib.SequenceMatcher(None, left, right).get_opcodes():
        if tag in ("replace", "delete") and left[i1:i2].strip():
            left_only.append(left[i1:i2])
        if tag in ("replace", "insert") and right[j1:j2].strip():
            right_only.append(right[j1:j2])
    return left_only, right_only


def highlighted_pair(left, right):
    marked_left, marked_right = [], []
    for tag, i1, i2, j1, j2 in difflib.SequenceMatcher(None, left, right).get_opcodes():
        left_text = html.escape(left[i1:i2]).replace("\n", "<br>")
        right_text = html.escape(right[j1:j2]).replace("\n", "<br>")
        if tag == "equal":
            marked_left.append(left_text)
            marked_right.append(right_text)
        else:
            if left_text:
                marked_left.append(f"<mark>{left_text}</mark>")
            if right_text:
                marked_right.append(f"<mark>{right_text}</mark>")
    return "".join(marked_left), "".join(marked_right)


def md(text):
    return (text or "").replace("|", "\\|").replace("\n", "<br>")


def metric_text(score):
    parts = []
    if score["similarity"] is not None:
        parts.append(f"金标贴合 {score['similarity'] * 100:.1f}%")
    if score["redline_ok"] is not None:
        parts.append("红线通过" if score["redline_ok"] else "红线失败")
    if score["digit_ok"] is not None:
        parts.append("数字通过" if score["digit_ok"] else "数字失败")
    parts.append(f"相对输入 {score['input_character_delta']:+d} 字")
    if score["missing_must_keep"]:
        parts.append("缺失=" + ", ".join(score["missing_must_keep"]))
    if score["forbidden_present"]:
        parts.append("未清理=" + ", ".join(score["forbidden_present"]))
    return "；".join(parts)


def percentile(values, fraction):
    if not values:
        return 0
    ordered = sorted(values)
    index = min(round((len(ordered) - 1) * fraction), len(ordered) - 1)
    return ordered[index]


def aggregate(rows, label):
    completed = [row[label] for row in rows if row.get(label) and not row[label].get("error")]
    scored = [row[f"score_{label}"] for row in rows if row.get(f"score_{label}")]
    similarities = [item["similarity"] for item in scored if item["similarity"] is not None]
    redlines = [item["redline_ok"] for item in scored if item["redline_ok"] is not None]
    digits = [item["digit_ok"] for item in scored if item["digit_ok"] is not None]
    latencies = [item["latency_seconds"] for item in completed]
    return {
        "completed": len(completed),
        "errors": len(rows) - len(completed),
        "similarity": statistics.mean(similarities) if similarities else None,
        "redline_pass": sum(redlines),
        "redline_total": len(redlines),
        "digit_pass": sum(digits),
        "digit_total": len(digits),
        "latency_mean": statistics.mean(latencies) if latencies else 0,
        "latency_p50": percentile(latencies, 0.50),
        "latency_p95": percentile(latencies, 0.95),
    }


def write_report(path, cases, rows, metadata):
    summary_8b = aggregate(rows, "8b")
    summary_14b = aggregate(rows, "14b")
    auto_8b = sum(row["winner"] == "8B" for row in rows)
    auto_14b = sum(row["winner"] == "14B" for row in rows)
    auto_tie = len(rows) - auto_8b - auto_14b
    identical = sum(row["8b"].get("output") == row["14b"].get("output") for row in rows)
    diagnostic_rows = [row for row in rows if row["case"]["group"].startswith("真实")]
    diagnostic_identical = sum(
        row["8b"].get("output") == row["14b"].get("output")
        for row in diagnostic_rows
    )

    lines = [
        "# Qwen3-8B vs Qwen3-14B · 语音润色 A/B",
        "",
        "> 本文档供用户人工 Review。两个模型使用同一份 prompt、`temperature=0`、关闭思考、Q4_K_M，并在同一台机器上串行运行。自动分数只用于找风险，最终结论以人工判断为准；RAMC 金标本身反映既有 prompt 的整理偏好，不等同于客观盲评。",
        "",
        "## 测试配置",
        "",
        f"- 生成时间：{metadata['generated_at']}",
        f"- Prompt：`{metadata['prompt_path']}`，SHA256 `{metadata['prompt_sha256'][:12]}`",
        f"- 8B：`{metadata['models']['8b']['path']}`（{metadata['models']['8b']['size_gb']:.2f}GB，加载 {metadata['models']['8b'].get('load_seconds', 0):.1f}s）",
        f"- 14B：`{metadata['models']['14b']['path']}`（{metadata['models']['14b']['size_gb']:.2f}GB，加载 {metadata['models']['14b'].get('load_seconds', 0):.1f}s）",
        f"- 样本：{len(cases)} 条；真实诊断 {sum(c['group'].startswith('真实') for c in cases)} 条，RAMC {sum(c['group'].startswith('RAMC') for c in cases)} 条",
        "",
        "## 自动汇总",
        "",
        "| 指标 | Qwen3-8B | Qwen3-14B |",
        "|---|---:|---:|",
        f"| 成功输出 | {summary_8b['completed']} | {summary_14b['completed']} |",
        f"| 既有金标贴合度 | {summary_8b['similarity'] * 100:.1f}% | {summary_14b['similarity'] * 100:.1f}% |" if summary_8b["similarity"] is not None else "| 既有金标贴合度 | - | - |",
        f"| 红线字段 | {summary_8b['redline_pass']}/{summary_8b['redline_total']} | {summary_14b['redline_pass']}/{summary_14b['redline_total']} |",
        f"| 数字字面命中 | {summary_8b['digit_pass']}/{summary_8b['digit_total']} | {summary_14b['digit_pass']}/{summary_14b['digit_total']} |",
        f"| 平均延迟 | {summary_8b['latency_mean']:.2f}s | {summary_14b['latency_mean']:.2f}s |",
        f"| P50 / P95 延迟 | {summary_8b['latency_p50']:.2f}s / {summary_8b['latency_p95']:.2f}s | {summary_14b['latency_p50']:.2f}s / {summary_14b['latency_p95']:.2f}s |",
        "",
        f"自动指标暂判：8B {auto_8b} 条，14B {auto_14b} 条，持平/无金标 {auto_tie} 条。",
        f"两模型输出完全一致：{identical}/{len(rows)} 条；其中真实口述 {diagnostic_identical}/{len(diagnostic_rows)} 条。",
        "",
        "## 人工 Review 说明",
        "",
        "每条请勾选一个：`8B 更好 / 14B 更好 / 持平 / 两个都不行`。优先看是否改错意思、漏掉信息、改坏数字专名，再看去重复、断句和排版。",
        "",
    ]

    groups = []
    for case in cases:
        if case["group"] not in groups:
            groups.append(case["group"])
    row_by_id = {row["case"]["id"]: row for row in rows}
    sequence = 0
    for group in groups:
        lines.extend([f"## {group}", ""])
        for case in [item for item in cases if item["group"] == group]:
            sequence += 1
            row = row_by_id[case["id"]]
            out_8b = row["8b"].get("output", "")
            out_14b = row["14b"].get("output", "")
            left_only, right_only = fragments(out_8b, out_14b)
            marked_8b, marked_14b = highlighted_pair(out_8b, out_14b)
            difference = "完全一致" if out_8b == out_14b else (
                f"8B 独有：`{' / '.join(left_only[:8]) or '无'}`；"
                f"14B 独有：`{' / '.join(right_only[:8]) or '无'}`"
            )
            lines.extend([
                f"### {sequence}. {case['category']} · `{case['id']}`",
                "",
                f"来源：`{case['source']}`；金标状态：{case['gold_status']}",
                "",
                "| 原始输入 | Qwen3-8B | Qwen3-14B |",
                "|---|---|---|",
                f"| {md(case['dirty'])} | {md(out_8b)} | {md(out_14b)} |",
                "",
            ])
            if case.get("clean"):
                lines.extend([f"**参考金标：** {md(case['clean'])}", ""])
            lines.extend([
                f"**差异点：** {difference}",
                "",
                f"**8B 差异高亮：** {marked_8b}",
                "",
                f"**14B 差异高亮：** {marked_14b}",
                "",
                f"**8B 自动指标：** {metric_text(row['score_8b'])}；耗时 {row['8b']['latency_seconds']:.2f}s",
                "",
                f"**14B 自动指标：** {metric_text(row['score_14b'])}；耗时 {row['14b']['latency_seconds']:.2f}s",
                "",
                f"**自动暂判：** {row['winner']}",
                "",
                "**用户 Review：** - [ ] 8B 更好　- [ ] 14B 更好　- [ ] 持平　- [ ] 两个都不行",
                "",
                "**备注：**",
                "",
                "---",
                "",
            ])
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines), encoding="utf-8")


def save_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def mount_external_models_if_needed(model_path):
    if model_path != DEFAULT_14B or os.path.ismount(EXTERNAL_MODEL_MOUNT):
        return
    if not EXTERNAL_MODEL_BUNDLE.exists():
        return
    print(f"挂载外接模型容器：{EXTERNAL_MODEL_BUNDLE}")
    subprocess.run(
        ["hdiutil", "attach", "-nobrowse", str(EXTERNAL_MODEL_BUNDLE)],
        check=True,
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-8b", type=Path, default=DEFAULT_8B)
    parser.add_argument("--model-14b", type=Path, default=DEFAULT_14B)
    parser.add_argument("--server", type=Path, default=DEFAULT_SERVER)
    parser.add_argument("--diagnostics", type=Path, default=DEFAULT_DIAGNOSTICS)
    parser.add_argument("--no-ramc", action="store_true", help="只跑真实诊断和产品场景")
    parser.add_argument("--report", type=Path, default=OUTPUT_DIR / "Qwen3-8B-vs-14B.md")
    parser.add_argument("--results", type=Path, default=OUTPUT_DIR / "results.json")
    parser.add_argument("--fresh", action="store_true", help="忽略已有结果，全部重跑")
    args = parser.parse_args()

    mount_external_models_if_needed(args.model_14b)

    prompt_template = PROMPT_PATH.read_text(encoding="utf-8")
    prompt_hash = hashlib.sha256(prompt_template.encode("utf-8")).hexdigest()
    cases = collect_cases(args.diagnostics, include_ramc=not args.no_ramc)
    if not cases:
        raise SystemExit("没有可用样本")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    snapshot_path = OUTPUT_DIR / "cases.snapshot.jsonl"
    snapshot_path.write_text(
        "".join(json.dumps(case, ensure_ascii=False) + "\n" for case in cases),
        encoding="utf-8",
    )

    state = {"prompt_sha256": prompt_hash, "rows": {}}
    if args.results.exists() and not args.fresh:
        try:
            loaded = json.loads(args.results.read_text(encoding="utf-8"))
            if loaded.get("prompt_sha256") == prompt_hash:
                state = loaded
        except (json.JSONDecodeError, OSError):
            pass
    for case in cases:
        state["rows"].setdefault(case["id"], {"case": case})
        state["rows"][case["id"]]["case"] = case

    previous_models = state.get("metadata", {}).get("models", {})
    model_specs = [("8b", args.model_8b), ("14b", args.model_14b)]
    model_meta = {}
    for label, model_path in model_specs:
        if not model_path.exists():
            raise SystemExit(f"{label} 模型不存在：{model_path}")
        pending = [case for case in cases if not state["rows"][case["id"]].get(label)]
        model_meta[label] = {
            "path": str(model_path),
            "size_gb": model_path.stat().st_size / 1_000_000_000,
        }
        previous_load = previous_models.get(label, {}).get("load_seconds")
        if previous_load is not None:
            model_meta[label]["load_seconds"] = previous_load
        if not pending:
            continue
        print(f"\n[{label}] 启动模型：{model_path.name}，待跑 {len(pending)} 条")
        server = LocalServer(args.server, model_path, label, OUTPUT_DIR)
        try:
            server.start()
            model_meta[label]["load_seconds"] = server.load_seconds
            print(f"[{label}] 就绪，加载 {server.load_seconds:.1f}s")
            for index, case in enumerate(pending, 1):
                result = call_model(server.endpoint, format_prompt(prompt_template, case["dirty"]))
                state["rows"][case["id"]][label] = result
                save_json(args.results, state)
                status = "OK" if not result["error"] else f"ERROR {result['error']}"
                print(f"[{label}] {index:02d}/{len(pending)} {case['id']} {result['latency_seconds']:.2f}s {status}")
        finally:
            server.stop()
            time.sleep(2)

    rows = []
    for case in cases:
        row = state["rows"][case["id"]]
        if not row.get("8b") or not row.get("14b"):
            continue
        row["score_8b"] = score_output(case, row["8b"].get("output", ""))
        row["score_14b"] = score_output(case, row["14b"].get("output", ""))
        row["winner"] = comparison_winner(row["score_8b"], row["score_14b"])
        rows.append(row)
    metadata = {
        "generated_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "prompt_path": str(PROMPT_PATH.relative_to(PROJECT_DIR)),
        "prompt_sha256": prompt_hash,
        "models": model_meta,
    }
    state["metadata"] = metadata
    save_json(args.results, state)
    write_report(args.report, cases, rows, metadata)
    print(f"\n完成：{args.report}")
    print(f"原始结果：{args.results}")
    print(f"样本快照：{snapshot_path}")


if __name__ == "__main__":
    main()
