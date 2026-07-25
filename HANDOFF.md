# AI 接线员 · 项目交接文档（v2.0，给接力开发者）

> 最后更新：2026-07-25 · 由 AI 助手（Claude / Codex 接力）维护。
> 这份文档假设你**没看过之前的对话**。读完它 + `ARCHITECTURE.md` 就能上手。

---

## 0. 一句话现状

一个 macOS + Windows 工具：把**有线耳机的「播放/暂停」键**变成 AI 的「接通键」。

| 版本 | 状态 |
|---|---|
| **v1** | 已发布。纯按键映射——耳机键 → 任意快捷键。 |
| **macOS v2.0** | ✅ **已发布**（2026-07-12，GitHub Release `v2.0`）。纯本地语音输入：耳机键 → 录音 → 本地 ASR → 规则清理 →（可选）本地大模型润色 → 合成键入。全程离线。 |
| **Windows v2** | ⚠️ **代码完成、未验收**。整条语音链路已用 C# 复刻（约 5700 行，见 §10），但**从未在 Windows 机器上编译或运行过**——全部在 macOS 上盲写。真机验收清单见 `Commander-Windows/VERIFY.md`。 |

**你接手时最重要的三件事**：
1. **Windows v2 的真机验收是当前第一优先级**（§10）。macOS 上编译不了 WPF，所有 Windows 断言都还是"应该能行"，不是"验过能行"。
2. macOS v2 已上线，代码全部在 git 里；日常改动照常 `./build.sh` → 测 → commit。
3. 语音/大模型的**模型文件不在仓库里**，靠运行时按需下载，见 §4。

---

## 1. 怎么把它跑起来（5 分钟）

```bash
cd ~/Downloads/接线员
./build.sh          # 编译 arm64 + 签名 → AI接线员.app（约 30-60s）
open AI接线员.app
```

- 需要：**Apple Silicon Mac** + Xcode 命令行工具。**intel 编译不了**（vendor 里的 sherpa-onnx/llama.cpp 库是 arm64）。
- 首次运行要在「系统设置 → 隐私与安全性 → 辅助功能」勾选 AI接线员，并授权麦克风。
- ⚠️ **重编译后授权会不会丢**取决于签名：`sign.sh` 用本机的 Apple Development 证书（hash `67926F55...`，team `Q55P36RVFD`）签名，DR 稳定 → 授权跨重编译保留。换机器/换人开发要改 `sign.sh` 里的 `DEV_ID` 成你自己的（`security find-identity -v -p codesigning` 查）。

想跑语音：App 里「输入方式」选「本地大模型」→ 点「下载语音识别模型」→ 说话。开发机上模型已在本地目录（§4），直接能用。

---

## 2. 目录结构

```
接线员/
├── HANDOFF.md              ← 你在读的这个（总纲）
├── ARCHITECTURE.md         ← 代码架构详解（配合读）
├── DEVLOG.md               ← ⭐ 开发日志：按模块记录"何时因何需求做了什么"，每完成任务追加一条
├── build.sh                ← ⭐ 一键编译+签名（swiftc，非 Xcode）
├── sign.sh                 ← 稳定签名（Apple Dev 证书，授权不丢的关键）
├── KeyRelay/               ← ⭐ macOS 全部 Swift 源码
│   ├── KeyRelayApp.swift          入口 + 菜单栏 + 窗口生命周期
│   ├── ContentView.swift          全部 UI（~1000 行，SwiftUI）
│   ├── KeyMappingStore.swift      ⭐ 中枢状态机（ObservableObject）
│   ├── MediaKeyInterceptor.swift  CGEventTap 拦截耳机键 + 合成目标键
│   ├── VoiceController.swift      ⭐ 语音主流程（录音→转写→润色→插入）
│   ├── VoiceEngine.swift          ⭐ ASR + 规则 + LLM 润色（含 prompt）
│   ├── AudioRecorder.swift        AVAudioEngine 采音 + 重采样到 16k
│   ├── Rules.swift                口水词规则清理（纯规则，零幻觉）
│   ├── Hotwords.swift             科技术语大小写纠正（GitHub/ChatGPT…）
│   ├── VoiceHUD.swift             Listening 胶囊悬浮窗（Liquid Glass）
│   ├── ModelDownloader.swift      ⭐ 模型按需下载（多源/断点续传/校验）
│   ├── LlamaRunner.swift          ⭐ 内嵌 llama-server 子进程生命周期
│   ├── SherpaOnnx.swift           sherpa-onnx 官方 Swift 封装（勿手改）
│   ├── sherpa-c-api.h             sherpa-onnx C 头（bridging header）
│   ├── AccessibilityHelper.swift  辅助功能权限查询
│   ├── VisualEffectView.swift     NSVisualEffectView 包装
│   ├── Info.plist / *.entitlements
│   ├── {zh-Hans,en}.lproj/Localizable.strings   ← 双语文案
│   └── ToolIcons/*.png            输入法预设的官方图标
├── vendor/                 ← ⭐ 第三方运行时（arm64，已进仓库，见 §6）
│   ├── sherpa-onnx/lib/     libsherpa-onnx-c-api.dylib + libonnxruntime（ASR/标点）
│   └── llama.cpp/llama-b9873/  llama-server + 全套 dylib（LLM 润色）
├── eval/                   ← ⭐ 语音润色评测集 + 回归套件（本次交接新收拢）
│   ├── final.jsonl          56 条真实中文口语 + 人工金标准（评测基准）
│   ├── run_eval.py          ⭐ 一键回归：跑 56 条打分，改 prompt 必跑
│   ├── review.md            人工把关文档（含 4 条【不要用】）
│   ├── mined.jsonl/cases.jsonl  中间产物（挖掘/起草）
│   ├── prompts/prompt_v{1..4}.txt  ⭐ prompt 迭代史（v1=现行，v2-4=试过落败）
│   └── scripts/             ramc_mine.py(挖数据) / bench_asr.py(选ASR) / …
├── dmg-build/              ← DMG 打包（build.py + 背景图 + 安装指南）
├── 终版分发/               ← 打好的 DMG 产物
├── Commander-Windows/      ← ⭐ Windows v2（C#/WPF，~5700 行，⚠️ 未真机验收，见 §10）
│   ├── VERIFY.md            ⭐ Windows 真机验收清单（跑通它才算数）
│   └── setup_and_build.ps1  一键构建（自检 .NET 8 → 单文件自包含）
├── assets/                 ← logo / 截图 / demo.gif（⚠️ 仍是 v1 界面）/ 小红书素材
├── README.md               ← 面向用户，中英双版（README.md / README.en.md）
├── CHANGELOG.md            ← 已更新至 v2.0（2026-07-12 发布）
└── KeyRelay.xcodeproj/     ← ⚠️ 存在但不用它构建，build.sh 才是真构建
```

---

## 3. 语音处理管线（数据怎么流）

```
按耳机键(down) ─→ MediaKeyInterceptor 吞掉媒体键，触发 VoiceController.toggle()
                    │
          [voiceMode=本地大模型] 才走语音；否则是纯按键映射(v1 逻辑)
                    ↓
  第 1 次按：AudioRecorder 开始采音 + 预加载引擎(藏进说话间隙)
                    ↓  用户说话
  第 2 次按：停止采音 → 静音检测(全静默则丢弃)
                    ↓
  VoiceEngine.transcribeRaw = ASR(Qwen3 或 SenseVoice) + Hotwords.correct
                    ↓
        ┌─ 关润色：VoiceEngine.polish = Rules.clean(口水词,可关) + [SenseVoice时加标点模型]
        └─ 开润色：Rules.clean 先删口水词 → VoiceEngine.llmEnhance(Qwen3-8B 大模型)
                    ↓
  IsSecureEventInputEnabled? → 是则提示"隐私区域禁用"，否则
  VoiceController.insertText = CGEvent unicode 合成键入（⚠️不走剪贴板）
```

关键设计原则（改代码前必须理解）：
- **不碰剪贴板**：文字用 `keyboardSetUnicodeString` 合成键盘事件插入，避免污染用户剪贴板历史。
- **闲时卸载**：ASR 引擎和 llama-server 都是**用后闲置 5 分钟自动释放**（`engineIdleSeconds`/`idleSeconds`=300）。内存只在最近用过语音的 5 分钟内占用。
- **预加载藏延迟**：按下键开始录音时就并行加载模型，用户说话的几秒盖住冷加载。
- **LLM 延迟有硬边界**：模型最多等 12 秒就绪，实际润色按文本长度最多等 12~25 秒；超时立即走规则/标点结果，不能为了润色让整次听写卡到一分钟。请求同时限制最大输出 token。
- **只复用自己启动的 LLM 进程**：43817 上若存在 App 已失去句柄的 `llama-server`，下次启动前会先清理再重建；prompt cache 固定上限 256MB，防止强退后的残留服务长期积累缓存和内存压力。

---

## 4. 模型文件（⭐ 最容易踩坑，仔细读）

**模型不在 git 里**（几百 MB~4.7GB）。ASR 严格读取用户当前选择且由 App 管理的模型：

| 模型 | 用途 | 体积(下载) | App 运行读取位置 |
|---|---|---|---|
| **Qwen3-ASR-0.6B** | 高精度 ASR（现行主力） | 838MB | `~/Library/Application Support/AI接线员/models/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25/` |
| Silero VAD | Qwen3 长语音分段（必需） | 0.63MB | `.../models/silero-vad/silero_vad.onnx` |
| SenseVoice | 轻量 ASR（可选档） | 155MB | `.../models/sherpa-onnx-sense-voice-.../` |
| 标点模型 CT-transformer | 仅 SenseVoice 档需要 | 62MB | `.../models/sherpa-onnx-punct-.../` |
| Qwen3-8B GGUF (Q4_K_M) | LLM 润色 | 4.7GB | `.../models/llm/Qwen3-8B-Q4_K_M.gguf` |

- 下载逻辑全在 `ModelDownloader.swift`：多源（`ghfast.top` 镜像 → `gh-proxy` → GitHub 直连；GGUF 走 ModelScope → hf-mirror → HF），失败自动换源，GGUF 走 Range 断点续传，都有文件头/体积校验防镜像返回 HTML 假文件。
- 模型来源：ASR/标点/Silero VAD 是 `k2-fsa/sherpa-onnx` GitHub release 的 `asr-models`/`punctuation-models` tag；GGUF 是 `Qwen/Qwen3-8B-GGUF`。
- ASR 不再读取 `~/Downloads/voice-engine/...` 的开发副本，避免“界面未下载但实际仍能运行”以及选择与实际模型不一致。
- `~/Downloads/voice-engine/asr-ab/` 里还有选型时下的 FireRed 两个模型（选型已淘汰，可删省 2GB，见 §7 选型结论）。

---

## 5. 三个"外部依赖"的真相（避免重复踩坑）

1. **这台开发机 SIP 曾被关闭**→ macOS 拒绝弹任何权限框（`requestAccess` 秒返回 false、截图失败）。若遇到"麦克风/权限弹不出来"，先 `csrutil status`，disabled 就进恢复模式 `csrutil enable`。**这跟代码无关**，排查过一整轮，别再往代码里找。
2. **中国网络**：走 Clash 代理 `127.0.0.1:7897`。pip 用清华镜像、GitHub 用 ghfast 镜像、模型用 ModelScope。LAN 请求要 `--noproxy`。
3. **开发期 LLM 用内嵌 llama-server**（端口 43817），不是 ollama。测 prompt 时若 server 没起，手动拉：
   ```bash
   ~/Downloads/接线员/AI接线员.app/Contents/Frameworks/llama/llama-server \
     -m "~/Library/Application Support/AI接线员/models/llm/Qwen3-8B-Q4_K_M.gguf" \
     --host 127.0.0.1 --port 43817 -ngl 99 -c 4096 \
     --prio 1 --cache-ram 256 --jinja --no-webui &
   ```

   手动服务只用于独立评测；App 自己开始润色时会把没有所有权的 43817 监听进程视为残留并重建。

---

## 6. 发布流程（macOS v2.0 已按此发过一次）

```bash
./build.sh                       # 编译 + 签名
python3 dmg-build/build.py       # 出 DMG → 终版分发/
# 换一台 Mac 实测新用户全流程（这一步救过一次命，见 §5.4）
gh release create v2.x --title "…" --notes-file <说明> 终版分发/*.dmg 终版分发/*.exe
```

已定原则：
- **DMG 必须自带运行时**：用户下载即可运行，不装任何开发依赖。`build.sh` 已把 sherpa-onnx / llama.cpp 打进 `Contents/Frameworks/`。
- **vendor/ 进仓库**（~100MB dylib）：clone 即可编译，用户侧不受影响。
- **eval/ 进仓库**（纯文本），**模型文件绝不进仓库**（靠下载）。
- **发布是对外动作，务必先问用户再发。**

---

## 7. 关键技术决策与"为什么"（别推翻重来）

- **ASR 选 Qwen3-0.6B 不选 SenseVoice/FireRed**：实测中英混说 CER，Qwen3 7.4% vs SenseVoice 15.6%，且自带标点大小写（省掉标点模型）。FireRed2-AED 精度最高(4.7%)但 3GB内存/2.6s/全大写无标点，弃。做成了双档让用户选（内存瓶颈：Qwen3 峰值 2.2GB，建议 16GB 机型）。选型脚本 `eval/scripts/bench_asr.py`。
- **跑社区 ONNX 导出（sherpa-onnx）而非官方推理栈，是刻意决策**：权重就是官方的（转 ONNX + int8）。官方 Qwen3-ASR 是流式/离线统一模型，但流式推理要 vLLM（Mac 基本不支持）或 transformers（要拖 2~3GB Python+PyTorch 运行时、冷启动 5~15s、峰值内存 3~5GB）——与"DMG 下载即用、内嵌零依赖"的分发原则冲突。sherpa-onnx 一个 ~20MB C++ dylib 同时跑 Qwen3/SenseVoice/标点/VAD 四个模型，冷启动 1.9s（实测）、峰值 2.2GB。代价=放弃流式模式与单次 20 分钟长音频（VAD 切段已绕过，"伪流式"=录音中逐段后台转写可拿回大部分体验，见 backlog）。同一逻辑也适用于 LLM（官方 PyTorch → llama.cpp GGUF），这是端侧部署的行业标准做法（同 whisper.cpp 之于 Whisper），**别为了"用官方栈"推翻**。
- **LLM 润色选 Qwen3-8B(q4)**：五关盲测胜出（8b 全对、7b 丢过字、qwen3:4b 思考关不掉）。走 OpenAI 兼容 `/v1/chat/completions` + `chat_template_kwargs.enable_thinking=false`（qwen3 关思考的正确姿势）。
- **口水词用规则不用小模型**：小模型会改数字（"两点半"→"两点三刻"），规则零幻觉。Rules.swift 里"额"必须在句首才删（否则腰斩"金额/名额"）。
- **⭐ 润色 prompt 已到当前模型天花板（见 eval/README-EVAL.md）**：56 条真实口语回归，现行 prompt(v1) 93.6% 贴合人工金标准，且 **55/56 零越界改写**——诊断证明它站在"忠实度拉满、清理度取到不牺牲忠实的最大值"的帕累托最优点。试过的 v2/v3/v4（加规则/加例子/重构任务定义）**全部更低**，因为往"更敢删"推会让模型在不该删处也动手（v4 越界从 1 条暴涨到 16 条）。**结论：prompt 别再拧了，要突破得换更大模型(14B+)或微调。** 这套评测集就是为验证"换模型/微调有没有真提升"而建的。

---

## 8. 待办 backlog（按优先级，用户已表态的标注）

**P0 发布（就差这一步）**
- [x] 提交 v2 到 git ——2026-07-11 完成（含 vendor/、eval/）
- [x] 终刷 DMG + CHANGELOG v2 + GitHub Release ——2026-07-12 发布 `v2.0`
- [x] 换机自测新用户全流程 ——2026-07-12 通过（暴露并修掉了中国网络下载全灭的问题）
- [ ] **Windows v2 真机验收**（§10，当前第一优先级）
- [ ] 重录 demo.gif / 截图（现挂的仍是 v1 界面）

**P1 打磨（用户暂缓，非阻塞）**
- [x] Windows 版对齐 v2 语音 ——代码 2026-07-18~20 完成，⚠️ 未真机验收（§10）
- [x] ASR/标点移除开发目录回退；`LlamaRunner.serverBinary` 的源码开发回退仍使用当前用户 `~/Downloads/...`
- [ ] Sparkle 自动更新框架
- [x] 伪流式转写：录音中 VAD 每闭合一段就后台先转写，按停只剩尾段+润色（同模型零精度损失）——2026-07-10 已上线，详见 DEVLOG 模块 2

**P2 想做但需要资源**
- [ ] LLM 润色换 14B+ 模型或微调（唯一能突破 93.6% 的路径，见 §7）——评测集已备好
- [ ] Apple 公证（需要 $99/年开发者账号，用户暂不办 → 用户仍会看到 Gatekeeper 拦截，README 已教绕过）
- [ ] 自定义热词（读用户可编辑的 hotwords.txt）
- [ ] 键盘快捷键也能触发语音（现在必须有线控耳机）

**用户明确说过"不要做"的**：光标焦点找回（处理期间切走 App 文字会落别处，是合成键入的天性，用户知道且接受，别自作主张改）。

---

## 9. 记忆/上下文补充

前一位助手在 `~/.claude/projects/.../memory/` 里存了三条相关记忆（若你能读到）：
- `ai-commander-v2-voice.md` — v2 全程决策史（最详细）
- `machine-network-china-gotchas.md` — 这台机器的 SIP/代理/镜像坑
- 项目还有一份 `调研-MKT-agent产出.md`（营销调研）和 `assets/redbook/`（小红书素材）。

**联盟链接勿删**：README 里 Typeless(`?via=jonny`) 和 Native Union(淘宝客) 是有效的推广位。

---

## 10. ⭐ Windows v2（当前第一优先级：真机验收）

**状态：代码完成，从未在 Windows 上编译或运行过。** 全部在 macOS 上盲写——macOS 的 .NET SDK 缺 `Microsoft.NET.Sdk.WindowsDesktop.targets`，WPF 编不了。已做的验证只有：核心模块（不含 WPF）独立 `net8.0` 工程编译通过、25 个 C# 文件 Roslyn 语法零错误、依赖版本与 API 已对 NuGet 实包核验。**这些都不等于能跑。**

### 代码结构（`Commander-Windows/`，约 5700 行）

与 macOS 一一对应，命名刻意保持一致，便于对照：

| Windows | macOS 对应 | 说明 |
|---|---|---|
| `MediaKeyInterceptor.cs` | 同名 .swift | 低级键盘钩子拦截媒体键，`return 1` 吞掉不下传 |
| `AudioRecorder.cs` | 同名 | WASAPI 采音（NAudio）→ 16k 单声道 |
| `VoiceEngine.cs` | 同名 | sherpa-onnx C#：Qwen3-ASR / SenseVoice + VAD |
| `VoiceController.cs` | 同名 | 主流程：录音→转写→清理→润色→注入 |
| `Rules.cs` / `Hotwords.cs` | 同名 | 规则与热词表（逐条对齐 macOS） |
| `LlamaRunner.cs` / `LlmEnhancer.cs` | 同名 | 内嵌 llama-server（**CPU 版**）+ 润色 |
| `LlmPerformanceProbe.cs` | — | **Windows 独有**，见下 |
| `TextInserter.cs` | VoiceController.insertText | `SendInput` + `KEYEVENTF_UNICODE`，不碰剪贴板 |
| `ModelDownloadService.cs` / `ModelCatalog.cs` | ModelDownloader.swift | 多源下载、续传、校验 |
| `PipelineDiagnostics.cs` | 同名 | 全链路 trace |

### ⚠️ Windows 与 macOS 的根本差异：润色跑在 CPU 上

macOS 走 Metal GPU（Qwen3-8B Q4 约 25-40 tok/s），Windows 内嵌的是 **llama.cpp CPU x64 运行时**，普通笔记本上可能只有 3-8 tok/s——**差一个数量级**。直接照搬 macOS 的固定超时预算，慢机器上会每次都超时回退：用户下了 4.7GB，看着"润色中"转十几秒，最后什么也没得到。

**解法（已实现）**：`LlmPerformanceProbe` —— 用户第一次打开润色开关时，用与真实请求同规模的负载实测一次本机吞吐（长 prompt 触发真实 prefill 成本，短 prompt 会把慢机器误判成快机器），按预计单句耗时分三档：

| 预计耗时 | 判定 | 行为 |
|---|---|---|
| ≤ 12s | Good | 正常开启 |
| 12~30s | Slow | 开启，但明确告知"每句约 N 秒" |
| > 30s | TooSlow | **不开启**，说明原因并告知语音识别不受影响 |

结果存进 Settings（只测一次），运行时超时预算也按实测吞吐算，不再拍脑袋。探测失败不拦人，交给运行时超时兜底。

### 真机验收

**照 `Commander-Windows/VERIFY.md` 一条条过。** 那份清单是这套代码唯一的真相来源——在它跑通之前，本节所有描述都只是设计意图。

构建：
```powershell
cd Commander-Windows
.\setup_and_build.ps1          # 自检 .NET 8 SDK → publish 单文件自包含 win-x64
```
