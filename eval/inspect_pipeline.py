#!/usr/bin/env python3
"""查看 AI 接线员最近一次（或指定）语音管线诊断会话。仅使用 Python 标准库。"""

import argparse
import difflib
import json
import os
from pathlib import Path


DEFAULT_ROOT = Path.home() / "Library/Application Support/AI接线员/diagnostics"


def session_dirs(root: Path):
    if not root.exists():
        return []
    return sorted(
        (path for path in root.iterdir() if path.is_dir() and (path / "trace.json").exists()),
        key=lambda path: path.name,
        reverse=True,
    )


def load_trace(directory: Path):
    with (directory / "trace.json").open(encoding="utf-8") as handle:
        return json.load(handle)


def text_delta(before: str, after: str):
    added = removed = 0
    for tag, i1, i2, j1, j2 in difflib.SequenceMatcher(None, before, after).get_opcodes():
        if tag in ("replace", "delete"):
            removed += i2 - i1
        if tag in ("replace", "insert"):
            added += j2 - j1
    return added, removed


def format_mapping(mapping):
    if not mapping:
        return ""
    return " · ".join(f"{key}={value}" for key, value in sorted(mapping.items()))


def print_trace(directory: Path, trace, full_prompt: bool):
    settings = trace.get("settings", {})
    print(f"会话: {trace.get('sessionID')}")
    print(f"时间: {trace.get('startedAt')}  →  {trace.get('completedAt') or '未完成'}")
    print(f"模型: {trace.get('model')}  ({trace.get('modelDirectory')})")
    print(f"设置: cleanFillers={settings.get('cleanFillers')} · useLLM={settings.get('useLLM')}")
    print(f"目标: {trace.get('targetAppName') or 'unknown'} · {trace.get('targetAppBundleID') or 'unknown'}")
    print(f"结果: {trace.get('outcome') or 'in_progress'}")
    print("=" * 88)

    previous_text = None
    for index, stage in enumerate(trace.get("stages", []), 1):
        name = stage.get("name", "unknown")
        at_ms = stage.get("atMs", 0)
        duration = stage.get("durationMs")
        characters = stage.get("textCharacters")
        suffix = []
        if duration is not None:
            suffix.append(f"耗时={duration:.1f}ms")
        if characters is not None:
            suffix.append(f"字符={characters}")
        metrics = format_mapping(stage.get("metrics"))
        details = format_mapping(stage.get("details"))
        if metrics:
            suffix.append(metrics)
        if details:
            suffix.append(details)
        print(f"[{index:02d}] +{at_ms:8.1f}ms  {name}" + ("  |  " + " · ".join(suffix) if suffix else ""))

        text = stage.get("text")
        if text is None:
            continue
        if name == "llm.prompt" and not full_prompt:
            print(f"     <完整 prompt 已省略；使用 --full-prompt 查看，共 {len(text)} 字符>")
        else:
            if previous_text is None:
                delta = "首个文本检查点"
            elif text == previous_text:
                delta = "与上一文本检查点相同"
            else:
                added, removed = text_delta(previous_text, text)
                delta = f"相对上一文本检查点 +{added} / -{removed}"
            print(f"     {delta}")
            print("     " + text.replace("\n", "\n     "))
        previous_text = text
    print("=" * 88)
    print(f"音频: {directory / 'input.wav'}")
    print(f"原始 JSON: {directory / 'trace.json'}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("session", nargs="?", help="会话目录、trace.json 路径或会话 ID；默认最新一次")
    parser.add_argument("--root", type=Path, default=Path(os.environ.get("AI_OPERATOR_TRACE_DIR", DEFAULT_ROOT)))
    parser.add_argument("--list", action="store_true", help="列出已有诊断会话")
    parser.add_argument("--full-prompt", action="store_true", help="显示完整 LLM prompt")
    args = parser.parse_args()

    directories = session_dirs(args.root)
    if args.list:
        if not directories:
            print(f"没有诊断会话：{args.root}")
            return
        for directory in directories:
            trace = load_trace(directory)
            print(
                f"{directory.name}  model={trace.get('model')}  "
                f"outcome={trace.get('outcome') or 'in_progress'}  "
                f"started={trace.get('startedAt')}"
            )
        return

    if args.session:
        candidate = Path(args.session).expanduser()
        if candidate.name == "trace.json":
            directory = candidate.parent
        elif candidate.is_dir():
            directory = candidate
        else:
            directory = args.root / args.session
    else:
        if not directories:
            raise SystemExit(f"没有诊断会话：{args.root}")
        directory = directories[0]
    if not (directory / "trace.json").exists():
        raise SystemExit(f"找不到 trace.json：{directory}")
    print_trace(directory, load_trace(directory), args.full_prompt)


if __name__ == "__main__":
    main()
