#!/usr/bin/env python3
"""Run multiple prompt files against multiple eval sets.

Usage:
  python3 run_prompt_matrix.py
  python3 run_prompt_matrix.py --cases final.jsonl multilingual.jsonl --prompts prompts/prompt_v1.txt prompts/prompt_v5_first_principles.txt

Prerequisite: llama-server is running at 127.0.0.1:43817.
"""
import argparse
import os
import subprocess
import sys

D = os.path.dirname(os.path.abspath(__file__))

DEFAULT_CASES = [
    "final.jsonl",
    "multilingual.jsonl",
]

DEFAULT_PROMPTS = [
    "prompts/prompt_v1.txt",
    "prompts/prompt_v5_conservative.txt",
    "prompts/prompt_v5_first_principles.txt",
    "prompts/prompt_v5_structured.txt",
    "prompts/prompt_v6_hybrid_multilingual.txt",
    "prompts/prompt_v7_minimal_numbers.txt",
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cases", nargs="+", default=DEFAULT_CASES)
    ap.add_argument("--prompts", nargs="+", default=DEFAULT_PROMPTS)
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    for case_file in args.cases:
        for prompt_file in args.prompts:
            print("\n" + "#" * 72, flush=True)
            print(f"# cases={case_file}  prompt={prompt_file}", flush=True)
            print("#" * 72, flush=True)
            cmd = [
                sys.executable,
                os.path.join(D, "run_eval.py"),
                "--cases",
                case_file,
                "--prompt",
                prompt_file,
            ]
            if args.verbose:
                cmd.append("-v")
            subprocess.run(cmd, cwd=D, check=False)


if __name__ == "__main__":
    main()
