# Windows v2 真机验收清单

> **为什么需要这份清单**：这套 Windows 代码全部在 macOS 上写成，从未在 Windows 上编译或运行过。
> macOS 的 .NET SDK 缺 `Microsoft.NET.Sdk.WindowsDesktop.targets`，WPF 编不了；WASAPI 录音、
> 键盘钩子、`SendInput` 注入、sherpa/llama 原生 DLL 装载，**没有一样能在 Mac 上验证**。
>
> 已完成的验证仅限于：核心模块（不含 WPF）独立 `net8.0` 编译通过、25 个 C# 文件 Roslyn 语法零错误、
> 依赖版本与 API 已对 NuGet 实包核验（`org.k2fsa.sherpa.onnx` 1.13.4 确有 `Qwen3Asr`，
> net8.0 依赖组含 `runtime.win-x64` 原生包）。**这些都不等于能跑。**
>
> 用法：在 Windows x64 机器上从上往下过，每条打勾。任何一条挂了，记进 `DEVLOG.md` 模块 1/2/3 再修。

---

## 0. 构建（先过这关，否则后面免谈）

- [ ] 装 .NET 8 SDK，`cd Commander-Windows && .\setup_and_build.ps1`
- [ ] **编译零错误**（macOS 上从未编译过 XAML，`x:Name` 与 code-behind 的绑定、事件签名都可能对不上）
- [ ] 产物 `publish\Commander.exe` 存在，单文件、自包含
- [ ] 双击能启动，不报缺 .NET / 缺 DLL
- [ ] 托盘图标出现，主窗口可开可关，关闭=隐藏不退出，单实例生效

**最可能挂的地方**：XAML 里引用了 code-behind 不存在的成员；WPF-UI 4.3.0 的控件命名空间与用法；`ui:ToggleSwitch` 等控件的事件签名。

> **已踩过的坑（2026-07-25）**：`error MSB4018: "GenerateBundle" 任务意外失败` +
> `IOException: ...publish\Commander.exe because it is being used by another process`。
> **编译其实是成功的**（看它前面那行 `0 个警告 0 个错误`），倒在最后的单文件打包——
> 上一版 `Commander.exe` 还在运行（含托盘），覆写不了。构建脚本现在会自动关掉旧进程，
> 若仍被占用会明确报"文件被锁"而不是甩一串 MSB4018 堆栈。
>
> **另外：别在 NAS / 网络路径上构建。** 当时的路径是 `\\VV_NAS\Others\AI接线员\...`，
> SMB 的文件锁和延迟写入会让单文件打包偶发失败。先 `robocopy` 到本地盘（如 `C:\dev\`）再构建，
> 脚本检测到 UNC 路径会给出警告。

---

## 1. 原生库装载（单文件发布的经典坑）

- [ ] 首次调用语音功能时 **sherpa-onnx 原生 DLL 能装载**（`sherpa-onnx-c-api.dll` / `onnxruntime.dll`）
      —— `IncludeNativeLibrariesForSelfExtract=true` 已开，但要确认解压目录可写、杀软不拦
- [ ] 开润色时 **llama-server 能从内嵌 zip 解出并启动**（`Resources\llama-b9873-bin-win-cpu-x64.zip`）
- [ ] 若报 `DllNotFoundException` 或 `0xc000007b`：多半是 x86/x64 混用或 VC++ 运行库缺失，记下具体报错

---

## 2. 耳机键（产品的命根子）

- [ ] 插有线耳机，按播放键 → App 有反应
- [ ] **媒体键被吞掉**：按下时正在播放的音乐**不会**暂停（钩子 `return 1` 是否真生效）
- [ ] 关掉总开关后，播放键**恢复**控制音乐（钩子完整卸载）
- [ ] 按键映射模式：选一个预设（如 Typeless），按耳机键 → 目标 App 收到对应快捷键
- [ ] 0.5 秒防抖：快速连按两下 = 只触发一次
- [ ] 蓝牙耳机 / USB 耳机是否也能触发（记录结果，写进 README 的兼容性说明）

---

## 3. 录音与识别

- [ ] 首次使用弹麦克风权限（Win10/11 隐私设置里 App 需被允许）
- [ ] **WASAPI 采音正常**：说话后 trace 里 `audio.captured_16k` 的 peak/rms 不为 0
- [ ] 换默认输入设备（拔插耳机）后仍能录到音
- [ ] 下载 Qwen3 模型 → 说一段话 → **文字正确**
- [ ] 下载 SenseVoice 档 → 同样可用；两档切换不串味
- [ ] 长口述（30 秒以上、带思考停顿）→ VAD 正常分段，内容不截断
- [ ] 中英混说、数字、日期 → 对照 macOS 的表现，明显差异要记下来

---

## 4. 文字注入

- [ ] 记事本、微信、浏览器输入框、VS Code —— 文字都能落进光标
- [ ] **中文与 emoji 正确**（`SendInput` + `KEYEVENTF_UNICODE`，代理对逐 UTF-16 单元发送）
- [ ] **剪贴板未被污染**（注入前后剪贴板内容不变）
- [ ] 输入过程中不吞字、不乱序（长文本分批发送的边界）
- [ ] 管理员权限窗口（如任务管理器）中注入失败是预期行为——确认不崩溃、有提示

---

## 5. 润色（Windows 特有风险，重点测）

Windows 跑 **CPU 推理**，与 macOS 的 Metal GPU 差一个数量级，`LlmPerformanceProbe` 就是为此而写。

- [ ] 第一次打开润色开关 → 出现"正在评估本机运行速度…" → 探测完成
- [ ] 探测结论与实际体感一致（trace 里看 `prefill` / `generation` tok/s）
- [ ] **慢机器**（如 4 核笔记本）判定为 TooSlow → **开关自动关掉 + 弹窗说明**，且明确告知语音识别不受影响
- [ ] **快机器**（如 8 核以上桌面）判定 Good/Slow → 润色真的出结果，不是每次超时
- [ ] 超时回退：真超时了要落规则结果，不能空白、不能卡死
- [ ] 关掉开关 → llama-server 进程退出；闲置 5 分钟 → 自动退出
- [ ] 强杀 App 后重开 → 不会留下孤儿 llama-server（macOS 上这个坑养出过一个活 12 天的僵尸进程）

**建议同时在两台机器上测**：一台高配、一台低配老笔记本。低配那台才是这个功能的试金石。

---

## 6. 模型下载（中国网络下最容易全灭）

macOS 上踩过：开发机挂代理一切正常，用户裸网 GitHub 系镜像**全线零速度**，只有 ModelScope/hf-mirror 能用。

- [ ] **关掉所有代理**，用真实家庭宽带测
- [ ] Qwen3 / SenseVoice / 标点 / VAD / GGUF 各自都能下完
- [ ] 断网重连 → 断点续传生效，不从头再来
- [ ] 取消按钮立即生效
- [ ] 磁盘空间不足时有明确提示
- [ ] `tar.exe` 解压 `.tar.bz2` 正常（Win10 1803+ 自带，老系统要确认）
- [ ] 下载中途强杀 App → 重开不会加载到半截模型（`.installing` 原子换目录是否可靠）

---

## 7. 收尾

- [ ] 开机自启勾选后重启电脑，确认真的自启
- [ ] 中英文界面切换正常
- [ ] SmartScreen / 杀软拦截情况记录下来（写进 README 的首次打开说明）
- [ ] 全流程走一遍诊断 trace，确认 `PipelineDiagnostics` 落盘正常

---

## 验收通过后

1. 把实测结果（尤其是**低配机器的润色耗时**和**耳机键兼容性**）写进 `DEVLOG.md`
2. 更新 `README.md` / `README.en.md` / `CHANGELOG.md`——目前它们都还写着"Windows 仅耳机键映射"
3. 打包发布前先问用户
