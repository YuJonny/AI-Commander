# Qwen3-8B vs Qwen3-14B A/B

`run_llm_ab.py` 会自动收集：

1. `~/Library/Application Support/AI接线员/diagnostics/` 里的真实口述，优先使用 `llm.input`；
2. `eval/product_scenarios.draft.jsonl` 里的产品场景与待确认金标；
3. `eval/final.jsonl` 的 56 条 RAMC 人工金标准。

两个模型使用同一个 `eval/prompts/prompt_v1.txt`、`temperature=0`、关闭思考，并串行加载，避免同时占内存。

14B 默认放在外接盘的稀疏 APFS 容器中：

- 容器：`/Volumes/UNTITLED/mac mini extra/AIReceptionistModels.sparsebundle`
- 挂载点：`/Volumes/AIReceptionistModels`
- 模型：`AI接线员/models/llm-ab/Qwen3-14B-Q4_K_M.gguf`

脚本发现外接盘已连接但容器尚未挂载时会自动挂载；外接盘未连接时会明确报模型不存在。这样可以绕开该盘 FAT32 的 4GB 单文件上限，也不会格式化或改动盘内原有内容。

```bash
python3 eval/run_llm_ab.py
```

产物：

- `Qwen3-8B-vs-14B.md`：给用户 Review 的并排报告；
- `results.json`：完整输出、耗时和自动指标，可断点续跑；
- `cases.snapshot.jsonl`：本轮实际使用的样本快照；
- `server-{8b,14b}.log`：两个 llama-server 的本地日志。

以后有新的真实口述时再次运行同一命令：旧结果会复用，只处理新增会话。Prompt 变化时哈希不一致，会自动创建本轮的新结果状态；需要强制全部重跑时加 `--fresh`。
