AI接线员 Commander v2.1 - Windows
=================================

定位
----
用耳机播放键启动本地听写，也可把播放键映射到常用语音工具的官方快捷键。
录音、语音识别和可选的大模型润色均可在本机完成。

技术规范
--------
- .NET 8 + WPF
- WPF-UI 4.3 / Fluent Design 2
- 主窗口使用 Mica，瞬时听写反馈使用 Acrylic
- H.NotifyIcon.Wpf 托盘与隐藏到后台
- WH_KEYBOARD_LL 监听媒体键，SendInput 注入快捷键或 Unicode 文本
- NAudio WASAPI 录音，sherpa-onnx 负责 VAD、ASR 与标点
- 内置 llama.cpp Windows CPU 运行时，承载可选 Qwen3-8B 本地润色

功能
----
- 两档本地语音模型：Qwen3 高精度、SenseVoice 轻量
- 所选模型未下载时严格阻止听写，不静默切换到其他模型
- Qwen3、SenseVoice、语义增强模型可同时下载，各自显示进度并可单独取消
- 预置 Windows 语音输入、微信语音输入、Typeless、Wispr Flow、Claude Code、Codex 快捷键
- 自定义单键或组合键，并支持按住触发与单击切换
- 口语词清理、常用科技热词校正、可选本地大模型润色
- 润色从录音开始预热；加载或生成超出延迟预算时自动回退，不让听写长时间卡住
- 听写状态浮层、托盘后台运行、开机自启动、中英文界面
- 可选逐阶段诊断：保存本地录音、ASR、规则、标点、LLM 和插入结果

构建要求
--------
- Windows 10 2004 或更高版本；Windows 11 可显示完整 Mica/Acrylic 效果
- .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
- x64 CPU

构建
----
1. 双击“一键编译.bat”。
2. 或在 PowerShell 中运行 setup_and_build.ps1。
3. 输出为 publish\Commander.exe。

脚本优先使用华为云中国大陆 NuGet 镜像，失败后再回退 NuGet 官方源。启动前会在
15 秒内逐个检测两个源，并按顺序还原依赖，不会同时等待多个源。若两个源均不可达，
会提示输入本地 HTTP 代理地址，例如
http://127.0.0.1:7890。也可以从 PowerShell 显式运行：

  .\setup_and_build.ps1 -Proxy http://127.0.0.1:7890

若曾卡在“正在确定要还原的项目”，可按 Ctrl+C 安全取消，再运行新版脚本。
新版会把依赖还原和正式编译分开，并显示具体失败的源或包。
首次还原需要下载依赖；成功后会缓存在 %USERPROFILE%\.nuget\packages，后续编译通常更快。

产物是 win-x64 自包含单文件，用户电脑无需预装 .NET。llama.cpp 运行组件已内置，
用户无需再从 GitHub 下载；语音与润色模型权重仍按需下载到
%LOCALAPPDATA%\AIReceptionist\models。

本地数据
--------
- 设置：%APPDATA%\Commander\settings.json
- 模型：%LOCALAPPDATA%\AIReceptionist\models
- 应用日志：%LOCALAPPDATA%\AIReceptionist\commander.log
- 诊断：%LOCALAPPDATA%\AIReceptionist\diagnostics（默认关闭，最多保留 50 次）

主要代码
--------
- VoiceController.cs       完整听写链路与状态机
- AudioRecorder.cs         WASAPI 录音、单声道下混与 16kHz 重采样
- VoiceEngine.cs           Qwen3 / SenseVoice、VAD、热词与标点
- ModelDownloadService.cs  并发下载、断点续传、低速切源、校验和原子安装
- LlamaRunner.cs           本地 llama-server 生命周期、残留清理与崩溃回收
- PipelineDiagnostics.cs   各阶段可追踪诊断
- MediaKeyInterceptor.cs   媒体键监听与快捷键注入
- FunctionPage.xaml        Fluent 设置界面
- VoiceHud.xaml            Acrylic 听写状态浮层

注意
----
构建、签名、打包和发布是独立步骤。本脚本只生成本地 publish 目录，不会上传或发布。
