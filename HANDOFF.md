# AI 接线员 · 项目交接文档（v2.0，给接力开发者）

> 最后更新：2026-07-08 · 由前一位 AI 助手（Claude）整理，交给 Codex 接力。
> 这份文档假设你**没看过之前的对话**。读完它 + `ARCHITECTURE.md` 就能上手。

---

## 0. 一句话现状

一个 macOS + Windows 工具：把**有线耳机的「播放/暂停」键**变成 AI 的「接通键」。
- **v1（已发布，Windows + 老 mac）**：纯按键映射——耳机键 → 任意快捷键。
- **v2（本仓库当前状态，仅 macOS / Apple Silicon，⚠️ 尚未 commit、未发布）**：在 v1 基础上加了**纯本地语音输入**——耳机键 → 录音 → 本地 ASR 转写 → 规则清理 → （可选）本地大模型润色 → 合成键入到当前 App。全程离线。

**你接手时最重要的三件事**（详见 §6）：
1. v2 的所有代码**都还没提交到 git**（`git status` 里一堆 `??` 和 ` M`）。
2. 项目能编译能跑（`./build.sh` 一键出签名 app），但**没发布**（DMG 待终刷、GitHub Release 待发）。
3. 语音/大模型的**模型文件不在仓库里**，靠运行时按需下载或开发期本地目录，见 §4。

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
├── vendor/                 ← ⭐ 第三方运行时（arm64，已在 .gitignore？见 §6）
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
├── Commander-Windows/      ← Windows 版（C#/WPF，v1，未随 v2 更新）
├── assets/                 ← logo / 截图 / demo.gif / 小红书营销素材
├── README.md               ← 面向用户（中英双语，已含 v2 功能+选型对比表）
├── CHANGELOG.md            ← ⚠️ 还停在 v1，需补 v2
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
     --host 127.0.0.1 --port 43817 -ngl 99 -c 4096 --jinja --no-webui &
   ```

---

## 6. ⭐ 你接手的第一优先级：提交 v2 到 git

现状：`git log` 最新是 v1，**v2 的 10 个新 Swift 文件 + vendor/ + eval/ 全是未跟踪状态**。建议：

```bash
cd ~/Downloads/接线员
git status                    # 先看清楚
# 检查 .gitignore 是否排除了 vendor/（27+72MB dylib）—— 决定 dylib 进不进仓库
cat .gitignore
```

决策点（需要你/用户拍板）：
- **DMG 分发原则已定**：最终给用户的 `.app` 必须自带 sherpa-onnx / llama.cpp 运行时，用户下载 DMG 后不需要装开发依赖即可运行。`build.sh` 当前已经把这些运行时打进 `Contents/Frameworks/`。
- **vendor/ 的 dylib（~100MB）进不进仓库？** 这只影响“别人 clone 源码后怎么编译”：进 → clone 即可编译，但仓库变大；不进 → 要写脚本让开发者自己下载 sherpa-onnx/llama.cpp release。无论哪种，DMG 都应保持自带运行时。
- **eval/ 的评测集要进仓库**（纯文本 112KB，是 README"诚实工程"叙事的硬料）。
- **模型文件绝不进仓库**（太大，靠下载）。

提交后就是发布三件套：终刷 DMG（`python3 dmg-build/build.py`）+ 审核 CHANGELOG + GitHub Release 挂 DMG。**发布是对外动作，务必先问用户再发。**

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
- [ ] 提交 v2 到 git（§6）
- [ ] 终刷 DMG + 审核 CHANGELOG v2 + GitHub Release（**发布前问用户**）
- [ ] 用户自测 DMG 走一遍新用户流程（装 /Applications → 授权 → 下载 → 说话）

**P1 打磨（用户暂缓，非阻塞）**
- [ ] Windows 版对齐 v2 语音（现在 Windows 只有 v1 按键映射）
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
