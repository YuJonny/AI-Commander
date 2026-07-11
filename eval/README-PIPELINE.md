# 全链路诊断

用于定位一次真实口述究竟在哪一层丢字、误改、断句或回退。它不是用户历史记录功能：默认关闭，只在开发机显式开启，数据仅保存在本机。

## 开启与关闭

```bash
# 开启（当前开发机）
defaults write com.vvclaw.commander pipelineDiagnosticsEnabled -bool true

# 关闭
defaults write com.vvclaw.commander pipelineDiagnosticsEnabled -bool false
```

也可只对一次从终端启动的 App 开启：

```bash
AI_OPERATOR_TRACE=1 ./AI接线员.app/Contents/MacOS/Commander
```

每次口述会生成：

```text
~/Library/Application Support/AI接线员/diagnostics/<会话ID>/
├── input.wav    # 实际送入 ASR 的 16k 单声道音频
└── trace.json   # 每个中间环节的文本、耗时、指标和分流结果
```

最多保留最近 50 次。正式分发默认不开启，因此不会保存普通用户的音频或正文。

## 查看最近一次

```bash
python3 eval/inspect_pipeline.py
python3 eval/inspect_pipeline.py --list
python3 eval/inspect_pipeline.py <会话ID>
python3 eval/inspect_pipeline.py --full-prompt
```

查看器会按时间输出每个阶段，显示文字长度、耗时、指标，以及相对上一文本检查点新增/删除了多少字符。

## 检查点

| 前缀 | 含义 | 出问题时先查 |
|---|---|---|
| `audio.*` | 原生采音、16k WAV、峰值/RMS、静音门限 | 麦克风、权限、音量、误触 |
| `engine.*` | ASR 引擎预加载与转写前是否复用 | 模型目录、冷启动、缓存 |
| `vad.*` | Qwen 分段数量和每段时长；SenseVoice 标记 bypass | 长语音漏段、尾段丢失 |
| `asr.segment.*` | 模型逐段直接输出 | 某一段识别错误或空结果 |
| `asr.joined_raw` | 逐段拼接后的纯 ASR 文本 | 分段拼接问题 |
| `terms.normalized` | 内置术语规范后的文本 | 专名大小写/分词误改 |
| `rules.*` | 每条确定性口语规则执行后的文本 | “这个”等词被规则误删 |
| `punctuation.*` | SenseVoice 标点模型输入、剥标点、模型输出、去重结果 | 错误断句、重复标点 |
| `llm.*` | LLM 输入、完整 prompt、HTTP、原始响应、清理后响应 | 润色误删、超时、服务失败 |
| `pipeline.*` | 使用哪条分支、是否发生 LLM 回退、最终文本 | 实际输出与预期管线不一致 |
| `insertion.*` | 目标 App、UTF-16 长度、键盘事件是否已发出 | 文本正确但没有写进输入框 |

最关键的定位规则：

1. `asr.joined_raw` 已经缺字：查音频、VAD 和 ASR。
2. ASR 有、`terms.normalized` 或 `rules.*` 开始缺：查确定性规则。
3. 规则后仍有、`llm.output` 缺：查 prompt 或 8B 模型。
4. `pipeline.final_text` 正确、目标输入框不对：查 `insertion.*` 和目标 App。

## 自动化测试

```bash
swiftc -sdk "$(xcrun --show-sdk-path)" -target arm64-apple-macosx13.0 \
  KeyRelay/PipelineDiagnostics.swift eval/scripts/test_pipeline_diagnostics.swift \
  -o /tmp/ai-operator-pipeline-diagnostics-test

AI_OPERATOR_TRACE=1 \
AI_OPERATOR_TRACE_DIR=/tmp/ai-operator-pipeline-diagnostics \
  /tmp/ai-operator-pipeline-diagnostics-test
```
