# AI 接线员 · 代码架构（配合 HANDOFF.md 读）

> 本文档 macOS 部分为主。**Windows v2 见文末「Windows 版架构」一节。**

面向"要改代码的人"。先读 `HANDOFF.md` 了解全局，本文讲每个模块**做什么、怎么连、改的时候注意什么**。

---

## 核心数据流：一次语音输入的完整调用链

```
KeyRelayApp (入口, NSApplicationDelegate)
  └ 创建 KeyMappingStore.shared（单例中枢）
       ├ interceptor: MediaKeyInterceptor   ← CGEventTap
       ├ voiceController: VoiceController    ← 语音主流程
       └ voiceHUD: VoiceHUD                  ← 悬浮窗

用户按耳机键(keyCode 16, systemDefined event)
  → MediaKeyInterceptor.handleEvent 捕获
  → 若 interceptor.voiceMode==true：吞掉媒体键 + 调 onVoiceTrigger()
       （onVoiceTrigger 在 KeyMappingStore.init 里接到 voiceController.toggle）
  → 若 voiceMode==false：走 v1 逻辑 simulateKey() 合成目标键
       │
VoiceController.toggle()  (isRecording 决定 start/stop)
  ├ startRecording → beginCapture:
  │    AudioRecorder.start() + preloadEngine()(并行加载模型)
  │    onState(.listening) → HUD 显示红点 Listening
  └ stopAndTranscribe:
       recorder.stop() → Capture（原生采音参数 + [Float] 16k 单声道）
       近静音检测(少于0.1s或 peak<0.008 && rms<0.0015 → onState(.noAudio))
       engine = VoiceEngine(modelDir)  (懒加载/缓存)
       base = engine.transcribeRaw(samples)   ← Qwen: Silero VAD 分段后逐段 ASR；SenseVoice: 整段 ASR；最后 Hotwords
       （开发诊断开启时：上述每步与后续规则/标点/LLM/键入写入同一 trace.json）
       if useLLM:
          onState(.enhancing) → HUD 紫点"LLM优化中"
          text = llmEnhance(Rules.clean(base)) ?? engine.polish(...)  (LLM 失败回退)
       else:
          text = engine.polish(base, clean: fillerRules)
       主线程: IsSecureEventInputEnabled? → insertText(text) 合成 unicode 键入
       onState(.inserted) → HUD 消失, statusText 更新
       scheduleEngineRelease()  (5 分钟后 engine=nil)
```

---

## 模块详解

### KeyMappingStore.swift —— ⭐ 中枢状态机
`ObservableObject` 单例，UI 和逻辑都挂它。关键 `@Published`：

| 字段 | 含义 |
|---|---|
| `isEnabled` | 总开关。didSet 启停 interceptor |
| `selectedInputMethod: InputMethodKind` | 当前输入方式。didSet 调 `applyInputMethod()` |
| `customTarget: TargetKeyInfo` | 自定义映射目标（支持组合键/双击） |
| `fillerRules` / `localLLM` | 口语词优化 / 大模型润色两个开关 |
| `qwenReady`/`senseReady`/`llmModelReady` | 各模型是否已下载（驱动 UI 按钮态） |
| `asrChoiceQwen` | ASR 二选一的持久化选择，也是运行时唯一模型依据 |
| `downloadingKind`/`downloadFraction` | 下载进度（驱动进度条） |

- `InputMethodKind` 枚举：`localLLM, custom, doubao, wechat, typeless, flashsay, wisprflow, superwhisper, claudecode, codex`（顺序=UI 展示顺序）。除前两个外都是"预设外部输入法"，选中即把耳机键映射到该工具默认热键（`presetTarget`）。
- `applyInputMethod()`：核心分发——localLLM 时 `interceptor.voiceMode=true`（走语音）；否则 false + 设置 `targetKey`（走按键映射）。
- 模型下载入口：`downloadQwenASR()` / `downloadSenseASR()` / `downloadLLMModel()`，删除：`deleteVoiceModels()` / `deleteLLMModel()`。

### MediaKeyInterceptor.swift —— 耳机键拦截
- `CGEvent.tapCreate` 监听 `systemDefined` 事件，耳机播放键 = keyCode 16。
- `voiceMode` 决定：true 吞键触发录音；false 走 `simulateKey()` 合成 `targetKeyCode`。
- `simulateKey` 支持三种目标：纯修饰键(flagsChanged)、普通键、**组合键**(⌘⇧Space)、**双击**(isDoubleTap，如 Codex ⌘⌘——后台线程发两次快速点击)。
- 同时用 `MPRemoteCommandCenter` 占住媒体控制，防止耳机键去控制别的播放器。
- 需要辅助功能权限；**授权后 tap 要重建才生效**（App 重启即可）。

### VoiceEngine.swift —— ⭐ ASR + 润色 + Prompt
- `VoiceController` 严格按 `asrChoiceQwen` 解析 Application Support 中的完整模型目录，不跨模型或回退开发目录；`VoiceEngine.init` 再按传入目录里的文件配置 Qwen3 或 SenseVoice。
- `transcribeRaw`：Qwen 先用 Silero VAD 按最长约 20 秒切段、逐段 ASR 再拼接；SenseVoice 继续整段 ASR；最后统一走 `Hotwords.correct`。Qwen `max_new_tokens=512`，与官方示例一致。
- `polish(text, clean)` = `Rules.clean`(可关) + 标点模型(仅SenseVoice)。
- `llmEnhance(text)` static = 拉起 `LlamaRunner` → 最多等 12 秒就绪 → POST `/v1/chat/completions` → **prompt 就写死在这个函数里**（⭐ 改润色行为改这里）。当前 prompt 是 `eval/prompts/prompt_v1.txt` 的内容。
- LLM 请求按输入长度使用 **12~25 秒延迟预算**，并以 `min(1024, max(64, 字符数×2+32))` 限制最大输出 token；超时/服务失败返回 `nil`，`VoiceController` 立即回退规则/标点管线，不允许单句一直等到 1 分钟。
- `llmWarmup()` = 录音开始时预热 llama-server。

### LlamaRunner.swift —— ⭐ 内嵌 LLM 子进程
- 把打包的 `Contents/Frameworks/llama/llama-server` 作为子进程拉起（端口 43817），管生命周期：`ensureRunning`(录音时预热) / `waitReady`(等健康检查) / 闲 5 分钟 `stop` / App 退出带走。
- **进程所有权规则**：`process` 句柄为空时，不再仅凭 `/health` 复用 43817 上的服务；启动前用“固定端口 + llama-server 进程名”清理脱离 App 的残留进程。否则 App 被强退一次，残留 server 会让 5 分钟回收永久失效。
- 参数：`-ngl 99`(全 Metal) `-c 4096` `--prio 1`(交互优先级) `--cache-ram 256`(prompt cache 上限 256MB，不能使用 llama.cpp 默认 8GB) `--jinja`(启用聊天模板→关思考) `--no-webui`。
- 权重走 mmap → 活动监视器里进程只显示几百 MB，5GB 权重记在系统文件缓存（这是正常的，别以为没加载）。

### ModelDownloader.swift —— ⭐ 按需下载
- `Item` 结构 = {目录名, 多个下载 URL, 校验文件, 是否压缩包, 体积}。
- ASR/标点是 tar.bz2（下载→解压→定位→落位）；Qwen 配套 Silero VAD 是约 0.63MB 的 ONNX 单文件；GGUF 是单大文件（**Range 断点续传**，`.part` 中间态，GGUF magic + 体积双校验）。
- 多源 fallback + HTTP 200 校验（防镜像 404 返回 HTML 假文件）。
- 落位到 `~/Library/Application Support/AI接线员/models/`。

### Rules.swift / Hotwords.swift —— 确定性清理
- `Rules.clean`：删明确口水词(嗯呃啊…)、折叠重复逗号、处理部分句首歧义词。**“这个/那个”不得进入确定性删除词表**，文本规则无法区分填充词与必要指代。⚠️"额"在 AMBIG(仅句首删)不在 FILLERS，否则腰斩"金额"。
- `Hotwords.correct`：科技术语大小写规范（github→GitHub），NSRegularExpression 白名单替换。词表在 `canonical`/`aliases` 两个数组。

### VoiceController.swift —— 语音总控
- `State` 枚举 = `.listening/.transcribing/.enhancing/.inserted/.noAudio/.noTranscript/.error`；音频信号过弱和 ASR 空结果使用不同提示。
- `insertText`：`CGEvent + keyboardSetUnicodeString` 分块合成（16 单元/块），**不走剪贴板**。
- `IsSecureEventInputEnabled()` 检测密码框 → 拒绝插入并提示。

### PipelineDiagnostics.swift —— 开发期全链路诊断
- 默认关闭；`defaults write com.vvclaw.commander pipelineDiagnosticsEnabled -bool true` 后，每次口述在 `~/Library/Application Support/AI接线员/diagnostics/<会话ID>/` 写 `input.wav + trace.json`，最多保留最近 50 次。
- 一个会话 ID 贯穿采音格式/信号门、引擎加载、VAD 每段、逐段 ASR、拼接、术语规范、`Rules.clean` 每个子步骤、SenseVoice 标点、LLM prompt/HTTP/原始响应/回退、最终文本和键盘事件。`llm.http_completed.metrics` 还记录输入/输出 token、缓存命中、prompt/生成耗时与各自 tok/s、延迟预算和最大输出 token。
- 每个检查点落盘一次，即使后续崩溃或超时，也能看到最后完成的阶段。正式功能不开启时 `begin()` 直接返回 nil，不保存用户音频或正文。
- 查看：`python3 eval/inspect_pipeline.py`；字段和定位方法见 `eval/README-PIPELINE.md`。

### ContentView.swift —— 全部 UI（~1000 行）
- 三个 tab：功能/关于/语言。功能页 = 总开关 + 输入方式(下拉+条件子视图) + 开机自启。
- 输入方式选"本地大模型"→ `localModelOptions`：ASR 二选一(`asrChoice`) + 润色开关 + 模型下载行；分段按钮直接标记“未下载”，标题状态写明具体缺 Qwen3 或 SenseVoice。
- 选外部输入法 → `mappingDetail`；选自定义 → 组合键录制。
- 文案全走 `L("key")` 本地化，key 在 `{zh-Hans,en}.lproj/Localizable.strings`。

### VoiceHUD.swift —— 悬浮胶囊
- `NSPanel`(borderless + nonactivating + 忽略鼠标)，不抢焦点。
- `render(state)`：listening→红点、transcribing→橙、enhancing→紫、error/noAudio/noTranscript→短暂 flash 后消失。
- macOS 26 用 `NSGlassEffectView`(真 Liquid Glass)，旧系统回退 `NSVisualEffectView`。宽度随文字自适应保持胶囊形。

---

## 构建系统（build.sh，不是 Xcode）

```
swiftc -O KeyRelay/*.swift
  -framework SwiftUI/AppKit/CoreGraphics/IOKit/Carbon/ApplicationServices/MediaPlayer/AVFoundation
  -import-objc-header KeyRelay/sherpa-c-api.h        ← C 桥接
  -L vendor/sherpa-onnx/lib -lsherpa-onnx-c-api      ← 链接 ASR 库
  -rpath @executable_path/../Frameworks              ← 运行时找 dylib
  -target arm64-apple-macosx13.0                     ← arm64 only
→ 组装 .app：拷 Info.plist / 图标 / .lproj / ToolIcons
→ 拷 dylib 到 Contents/Frameworks（sherpa-onnx 2个 + llama 全套）
→ sign.sh 用 Apple Dev 证书签名
```

`KeyRelay.xcodeproj` 存在但**不参与构建**，别被它误导。

---

## 评测系统（eval/，改 prompt/换模型必用）

```bash
# 前提：llama-server 在 43817 跑（App 开着润色，或手动拉，见 HANDOFF §5）
cd eval && python3 run_eval.py           # 跑现行 prompt；保护“这个/那个”后的基线为 92.8%
RAMC_PROMPT_FILE=prompts/prompt_v4.txt python3 run_eval.py   # 换 prompt 测
```
- `final.jsonl`：56 条真实中文口语(RAMC 挖的) + 人工金标准，5 类(自我更正/重复口吃/口水词/含数字/长句)。
- 指标：**贴合度**(1-归一化编辑距离 vs 金标准) + **数字保真**。
- 改 `VoiceEngine.llmEnhance` 里的 prompt 后，先 `run_eval.py` 看涨没涨再改代码。当前回归基线是 92.8%；旧 93.6% 是保护必要指代修复前的结果。

---

# Windows 版架构（`Commander-Windows/`）

> ⚠️ **代码完成，未真机验收**——全部在 macOS 上盲写，WPF 编译不了。验收清单见 `Commander-Windows/VERIFY.md`。
> 设计原则：**与 macOS 一一对应、命名刻意一致**，方便两边对照排查；已验证的行为参数（VAD 1.5s、
> 防抖 0.5s、闲置 5 分钟、1000 字符润色上限、prompt、规则表、热词表）直接照搬，不重新发明。

## 技术栈

| 层 | macOS | Windows |
|---|---|---|
| UI | SwiftUI + Liquid Glass | WPF + WPF-UI 4.3.0（Fluent 2：主窗口 Mica、HUD Acrylic） |
| 托盘 | NSStatusItem | H.NotifyIcon.Wpf 2.3.2 |
| 采音 | AVAudioEngine | NAudio 2.3.0（WASAPI） |
| ASR | sherpa-onnx C API + Swift 封装 | `org.k2fsa.sherpa.onnx` 1.13.4（NuGet，含 win-x64 原生包） |
| 键盘钩子 | CGEventTap | `SetWindowsHookEx(WH_KEYBOARD_LL)` |
| 文字注入 | `keyboardSetUnicodeString` | `SendInput` + `KEYEVENTF_UNICODE` |
| LLM | llama.cpp **Metal GPU** | llama.cpp **CPU x64**（内嵌 zip，运行时解出） |
| 打包 | DMG（自带 Frameworks） | 单文件自包含 exe（`IncludeNativeLibrariesForSelfExtract`） |

## 文件对应表

```
MediaKeyInterceptor.cs   ← 低级键盘钩子；命中媒体键后 return 1 吞掉，不下传给音乐 App
AudioRecorder.cs         ← WASAPI 采音 → 16k 单声道
VoiceEngine.cs           ← sherpa-onnx：Qwen3-ASR（+Silero VAD 分段）/ SenseVoice（+CT 标点）
VoiceController.cs       ← 主流程：录音 → 转写 → 规则 → 润色 → 注入；防抖、20 分钟上限、闲置释放
Rules.cs / Hotwords.cs   ← 与 macOS 逐条对齐的规则表与术语表
LlamaRunner.cs           ← 内嵌 llama-server 生命周期（127.0.0.1:43817，闲置 5 分钟退出）
LlmEnhancer.cs           ← 润色请求；超时预算取自 LlmPerformanceProbe 实测值
LlmPerformanceProbe.cs   ← ⭐ Windows 独有，见下
TextInserter.cs          ← SendInput Unicode 注入（逐 UTF-16 单元，代理对安全），不碰剪贴板
ModelCatalog.cs / ModelDownloadService.cs ← 模型目录与多源下载（续传/校验/取消/磁盘预检）
PipelineDiagnostics.cs   ← 全链路 trace（与 macOS 同名阶段，便于横向对比）
Settings.cs              ← 配置持久化（含探测结果）
```

模型落位：`%LOCALAPPDATA%\AIReceptionist\models`（macOS 是 `~/Library/Application Support/AI接线员/models`）。

## ⭐ `LlmPerformanceProbe`：Windows 独有的性能分级

**为什么 macOS 没有**：Mac 是 Metal GPU，Qwen3-8B Q4 稳定 25-40 tok/s，一个固定的 12~25 秒超时预算对所有 Apple Silicon 机器都成立。Windows 是 CPU 推理，从 4 核笔记本到 16 核桌面能差一个数量级——照搬固定预算的结果是：慢机器上每次润色都跑到超时回退，用户下了 4.7GB 却永远看不到润色结果。

**怎么做**：用户第一次打开润色开关时实测一次。

1. 发一个**与真实请求同规模**的负载（长 prompt + 生成 64 token）
   —— 关键：不能用短句探测，短 prompt 的 prefill 几乎免费，会把慢机器误判成快机器
2. 读 llama.cpp 返回的 `timings.prompt_per_second` / `predicted_per_second`
   （若服务未返回 timings，按墙钟兜底估算，避免整个探测失效）
3. 按典型工作量（约 740 prompt token + 70 输出 token）折算**预计单句耗时**，分三档：

   | 预计耗时 | 判定 | 行为 |
   |---|---|---|
   | ≤ 12s | Good | 正常开启 |
   | 12~30s | Slow | 开启，但明确告知"每句约 N 秒"，用户自行决定 |
   | > 30s | TooSlow | 不开启，弹窗说明原因并强调语音识别不受影响 |

4. 结果存 `Settings`（只测一次）；`LlmEnhancer` 的**超时预算按实测吞吐计算**而非硬编码
   （`RequestTimeoutSeconds`：按输入长度估算耗时 × 1.6 余量，夹在 12~90 秒之间）

**边界处理**：探测失败（服务起不来等）不拦用户，按未知处理、交给运行时超时兜底——探测本身不该成为新的失败点。

## 构建

```powershell
cd Commander-Windows
.\setup_and_build.ps1     # 自检 .NET 8 SDK → dotnet publish -r win-x64 --self-contained
                          #   -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
                          # 产物：publish\Commander.exe
```

模型不进 exe，仍按需下载；llama.cpp CPU 运行时以 zip 形式内嵌（约 17MB），首次用润色时解出。
