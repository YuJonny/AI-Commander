# 语音润色评测集 · 说明

这套东西是为回答一个问题而建的：**"我改了 prompt / 换了模型，润色质量到底是升是降？"**
不靠主观感觉，靠 56 条真实中文口语 + 人工金标准打分。

## 文件

| 文件 | 是什么 |
|---|---|
| `final.jsonl` | ⭐ **评测基准**：56 条 `{id, category, dirty(脏输入), clean(人工金标准), src}` |
| `multilingual.jsonl` | ⭐ **跨语言压力集**：中文 ASR 易错、中英混说、英文、日语、韩语、粤语；含 `must_keep` / `must_not_include` 红线字段 |
| `run_eval.py` | ⭐ **回归脚本**：把每条 dirty 喂 LLM，输出 vs clean 打分；支持 `--cases` / `--prompt` |
| `run_prompt_matrix.py` | ⭐ **prompt 矩阵对比**：批量跑多套 prompt × 多套 case |
| `review.md` | 人工把关的原始文档（含用户标的 4 条【不要用】和 21 处手改） |
| `mined.jsonl` | 中间产物：从 RAMC 挖出的原始脏句（未起草干净侧） |
| `cases.jsonl` | 中间产物：LLM 起草的干净侧（未经人工把关） |
| `prompts/prompt_v{1..5...}.txt` | ⭐ prompt 迭代史（见下）；v5 系列是 Codex 从第一性原理重写的候选 |
| `scripts/` | 一次性工具：`ramc_mine.py`(挖数据) `ramc_finalize.py`(合并人工把关) `bench_asr.py`(ASR选型) `acc_bench.py`/`llm_bench.py`(早期实验) |

## 怎么用

```bash
# 前提：llama-server 在 127.0.0.1:43817 跑（开着 App 的润色即可）
cd eval
python3 run_eval.py                                   # 跑现行 prompt
python3 run_eval.py -v                                # 逐条看输出
RAMC_PROMPT_FILE=prompts/prompt_v2.txt python3 run_eval.py   # 试别的 prompt
python3 run_eval.py --cases multilingual.jsonl --prompt prompts/prompt_v5_first_principles.txt
python3 run_prompt_matrix.py                          # v1 + v5 系列 × 中文/跨语言两套集
```
输出：平均贴合度、数字保真、红线字段保真、分类得分、差异最大的 8 条。中文 RAMC ~60 秒；跨语言小集更快。

`multilingual.jsonl` 的红线字段：
- `must_keep`：输出必须保留的关键词/数字/代码/专名。
- `must_not_include`：输出不得出现的被更正旧值、错误翻译或禁用表达。

## 数据从哪来

真实语料：**MagicData-RAMC**（180 小时中文自然对话，中科院声学所+上交+西工大开源，逐字转写含嗯呃/口吃/更正标注）。用户下到 `~/Downloads/MagicData-RAMC 2`（**不在本仓库**，351 组对话）。
`scripts/ramc_mine.py` 从中按 5 类现象采样 60 条 → LLM 起草干净侧 → 用户人工把关改了 21 处、剔 4 条 → `final.jsonl` 56 条。

## ⭐ 核心结论（省下你重复实验的时间）

**现行 prompt(v1) = 93.6% 贴合度，且 55/56 零越界改写——这是当前 8B 模型的帕累托最优点，别再拧 prompt 了。**

> 2026-07-10 保真修复：生产 v1 不再把“这个/那个”视为可无条件删除的口水词。定点测试已确认必要指代保留；下表 93.6% 是修复前结果，56 条全量分数待下次 prompt 回归时刷新。

迭代史（全部真跑过 56 条）：
| prompt | 贴合度 | 说明 |
|---|---|---|
| **v1（现行）** | **93.6%** | 忠实度拉满(仅1条越界)，19条留得比人工略多 |
| v2（正文加"模糊修正+数字量词"规则） | 92.1% | 目标短板修好了，但扰动其他类别，净输 |
| v3（v1+2个few-shot例子） | 91.0% | 同样负优化 |
| v4（重构任务定义为"大胆删绝不改"） | 91.4% | 越界改写从1条暴涨到16条 |
| v5-conservative | 83.0% | 抽象保守原则压住了模型，该删的不删、数字不转；跨语言 81.3%，但红线字段 9/18 |
| v5-first_principles | 84.9% | 中英混说尚可，但中文主集明显低于 v1；跨语言 80.4%，仍会翻译日/粤，红线字段 9/18 |
| v5-structured | 83.8% | 排版任务定义没有带来收益；跨语言 84.6%，但数字保真 0/8、红线字段 9/18 |
| v6-hybrid_multilingual | 89.1% | 保留 v1 具体规则 + 加多语言约束；中文仍低于 v1，跨语言 74.3%，外语仍会被翻译 |
| v7-minimal_numbers | 85.5% | 按用户建议压缩成"原则优先 + 数字格式小规则"；规则噪音少了，但 8B 执行力也下降，口吃/自我更正/数字转换都不如 v1 |

**根因诊断**（受控实验得出）：润色是一对矛盾指令——"清理干净" ⚔️ "一字不改"。8B 模型消解不了这个矛盾，越逼它敢删，它越会在不该删处也动手。v1 选择了"宁可少删几个赘词，绝不改用户原意/数字"，这在**输入法场景绝对正确**（改错一个字比留个"一种"糟糕百倍）。

**要突破 93.6% 的唯一路径**：换 14B+ 模型（有语义判断力）或微调专用小模型。**这套评测集就是为验证"换了到底有没有真提升"而备的**——别换了模型凭感觉说"好像好了点"，跑一遍 `run_eval.py`，数字说话。

## 2026-07-08 Codex 补充：第一性原理 prompt 实验

用户要求摆脱现有 prompt、从第一性原理重新设计口语润色 prompt，并覆盖中文、中英混说和外语场景。Codex 新增：
- `prompts/prompt_v5_conservative.txt`
- `prompts/prompt_v5_first_principles.txt`
- `prompts/prompt_v5_structured.txt`
- `prompts/prompt_v6_hybrid_multilingual.txt`
- `multilingual.jsonl`
- `run_prompt_matrix.py`

实测结论：
- **中文默认 prompt 仍应保留 v1**：v5/v6 全部低于 v1 的 93.6%，其中 v6 最接近但也只有 89.1%。
- **跨语言不能靠一个中文主 prompt 解决**：v1 中英混说很好，但会把英/日/韩/粤翻成中文；v5/v6 即使写了"不要翻译"，8B 模型仍会在外语样例上翻译或改写。
- **下一步方向不是继续把单 prompt 写长**：应考虑按语言/场景分流 prompt，例如中文/中英混说继续用 v1，检测到纯英文/日文/韩文/粤语时走更短、更强约束的同语言 prompt；或者在 UI 中先声明外语为 ASR 转写支持、润色重点仍是中文与中英混说。

用户随后建议：对 8B 模型不要堆规则，回到更短的第一性原理 prompt，只把数字格式稍微具体化。Codex 新增 `prompt_v7_minimal_numbers.txt` 并跑中文 RAMC，结果 85.5%，仍明显低于 v1。诊断：短原则更清爽，但这个 8B 模型需要具体 few-shot 才会执行口吃清理、自我更正和数字规范化；只给抽象原则会变成"保留原文为主"。

TODO：用户仍不满意现行 prompt，本轮先暂停。后续继续优化前，先补更贴近 AI 接线员真实使用的评测集，不要只围绕 RAMC 编辑距离调参。建议新增真实产品场景样本（写代码、给 AI 下指令、聊天、内容创作、中英技术词、数字/地址/专名红线），并加入 `must_keep` / `must_not_include`、多金标或人工盲评，降低"评测本身偏向 v1 风格"的风险。
