# AI 接线员 · 代码架构（配合 HANDOFF.md 读）

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
- `llmEnhance(text)` static = 拉起 `LlamaRunner` → POST `/v1/chat/completions` → **prompt 就写死在这个函数里**（⭐ 改润色行为改这里）。当前 prompt 是 `eval/prompts/prompt_v1.txt` 的内容。
- `llmWarmup()` = 录音开始时预热 llama-server。

### LlamaRunner.swift —— ⭐ 内嵌 LLM 子进程
- 把打包的 `Contents/Frameworks/llama/llama-server` 作为子进程拉起（端口 43817），管生命周期：`ensureRunning`(录音时预热) / `waitReady`(等健康检查) / 闲 5 分钟 `stop` / App 退出带走。
- 参数：`-ngl 99`(全 Metal) `-c 4096` `--jinja`(启用聊天模板→关思考) `--no-webui`。
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
- 一个会话 ID 贯穿采音格式/信号门、引擎加载、VAD 每段、逐段 ASR、拼接、术语规范、`Rules.clean` 每个子步骤、SenseVoice 标点、LLM prompt/HTTP/原始响应/回退、最终文本和键盘事件。
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
