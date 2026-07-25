<div align="center">

<img src="assets/logo.png" width="128" alt="AI 接线员 · AI Commander" />

# AI 接线员 · AI Commander

**用耳机上的「播放 / 暂停」键，一键接通你的 AI —— 开口说，文字直接落进光标。**

**随口说，出成稿** · 全程本地 · 为爱发电，永久免费

**中文** · [English](README.en.md)

![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows-lightgrey)
[![Release](https://img.shields.io/github/v/release/YuJonny/AI-Commander)](https://github.com/YuJonny/AI-Commander/releases)
![全程本地](https://img.shields.io/badge/语音处理-全程本地离线-3b82f6)

<img src="assets/screenshot-v2.jpg" width="440" alt="AI 接线员 v2 主界面" />

⭐ **觉得有用，点个 Star 支持一下**

<sub>语音输入 · 本地语音识别 · 本地大模型 · 离线 · 媒体键映射 · push-to-talk · voice input · vibe coding · 接线员 · macOS · Windows</sub>

</div>

---

## 缘起

以前打电话，是一件很具体的事。

听筒有重量，电话线会绕在手边。号码拨出去以后，要等另一头有人接起。电话把相隔很远的两个人接在一起，声音从一根细细的线里传过来。

后来，电话被收进一块薄薄的屏幕里。我们不再守在电话旁，等一个人。听筒悄悄退场，成了电影里的道具。

我一直很喜欢那个简单的动作：拿起听筒，线路接通，然后开口。

在这个时代，要是还有这样一部电话呢？

过去，电话把人与人连接在一起。

现在，我们用它，和未来说话。

---

## 主要能力

AI 接线员 v2 有两种独立的使用方式：在 Apple 芯片 Mac 上直接使用内置的本地语音输入；或只做耳机键映射，调用已经安装的语音工具。使用本地语音不需要安装 Typeless、微信输入法或任何第三方输入法。

- **耳机键一按即说**：按一下开始，再按一下结束，文字直接落进当前光标。
- **全程本地，永久免费**：识别、清理、润色全在本机，录音与文字不上传；开源可审计。
- **两档语音识别**：Qwen3-ASR 高精度中英混说；SenseVoice 轻量省内存。
- **随口说，出成稿**：规则层清口语词、纠术语大小写；可选本地大模型整理重复、标点与数字格式，忠实原意。
- **兼容已有语音工具**：内置豆包、微信、Typeless、闪电说、Wispr Flow、Superwhisper、AutoGLM、Claude Code、Codex 预设，也可自定义快捷键。

### Liquid Glass 设计

首个整体采用 Liquid Glass 设计的语音输入法。听写时悬浮胶囊随「录音 → 识别 → 润色」状态变化，完成即收起，不打断手头的事。

## 随口说，出成稿

下面是一段典型的工作口述，并用当前生产版管线实际处理。确定性规则先清理口语词，本地大模型再整理重复、数字和标点；关键信息保持不变。

| 阶段 | 输出 |
|---|---|
| **原始转写** | 嗯，我我看了一下，项目预算大概三千五百块，然后然后下周三上午十点半开会，订单号是零零八六二一，呃，地址是三号楼二单元。 |
| **规则清理** | 我我看了一下，项目预算大概三千五百块，然后下周三上午十点半开会，订单号是零零八六二一，地址是三号楼二单元。 |
| **本地大模型润色** | 我看了下，项目预算大概 3500 元，然后下周三上午 10:30 开会，订单号是 008621，地址是三号楼二单元。 |

## 模型与语言

| 组件 | 支持语言 | 擅长 | 大小 |
|---|---|---|---|
| **Qwen3-ASR · 高精度**（推荐） | 30 种语言 + 22 种中文方言 | 中英混说、长口述；16GB+ 机型 | 约 840MB |
| **SenseVoice · 轻量** | 中、英、粤、日、韩 | 日常口述；8GB 机型 | 约 220MB |
| **Qwen3-8B · 语义增强**（可选） | 中文为主，支持中英混说 | 去重复、数字格式、断句分段 | 约 4.7GB |

模型均按真实口述场景，通过本地数据及开源数据集，经过选型和调优。
只用按键映射时，无需下载任何模型。

---

## 下载与安装

前往 [Releases](https://github.com/YuJonny/AI-Commander/releases/latest) 下载。

| 平台 | 文件 | 能力 |
|---|---|---|
| **macOS**（Apple 芯片） | `AI.Commander_Mac_v2.1.dmg` | 本地语音输入 **＋** 耳机键映射 |
| **Windows** | `AI.Commander_Win.exe` | 耳机键映射 |

### macOS 首次打开

应用未做 Apple 公证，首次打开可能提示「已损坏」或「身份不明的开发者」。软件没问题，任选一种处理：

- **系统设置**：双击 App → 「系统设置 → 隐私与安全性」→ 点「仍要打开」。
- **终端兜底**：
  ```bash
  sudo xattr -cr /Applications/AI接线员.app
  ```

首次启动会引导授权**辅助功能**与**麦克风**，并可顺手检测耳机播放键。

### Windows 首次打开

- SmartScreen 拦截：点「更多信息 → 仍要运行」。
- 杀毒软件可能因「键盘钩子」误报，加入信任即可。
- 单文件免安装，x64，Windows 10/11，无需管理员权限和 .NET。

## 常见问题

### 按耳机键会不会同时触发语音输入和音乐播放 / 暂停？

不会。总开关开启后，AI 接线员会接管并吞掉这次播放键事件：本地语音模式只控制录音，按键映射模式只发送目标工具的快捷键。它不会一边启动 Typeless 一边暂停音乐，也不会在结束语音时自动恢复播放。

这也意味着，总开关开启期间，耳机播放键不再控制媒体；关闭总开关后，监听会完整释放，按键恢复原有的播放 / 暂停功能。这是明确的按键接管，不是两个操作同时触发的冲突。

### 搭配 Typeless 时，把耳机键映射成 Fn 会和其他快捷键冲突吗？

不会。Fn 是 Typeless 的默认语音快捷键；AI 接线员只会在你按耳机键时模拟 Fn，不会修改 Fn 原本的功能，也不会占用其他快捷键。直接选择 Typeless 预设即可；如果你改过 Typeless 的默认快捷键，再使用「自定义」映射。

### 搭配微信输入法，需要安装其他自动化脚本吗？

不需要。微信输入法 Mac 版提供 `Fn + 空格` 免提模式；AI 接线员的微信输入法预设会直接映射这个官方快捷键。按一下耳机键开始说，再按一下结束，操作逻辑和其他切换式语音工具一致，不需要 Keyboard Maestro、BetterTouchTool 或其他脚本。

### 第一次使用需要配置很多东西吗？

不需要。打开 App，按引导完成授权，再选择一种输入方式就能使用；不用写配置，也不用安装脚本。使用现有输入法无需下载模型，选择本地语音时再下载一个语音识别模型即可。

### 按耳机键没有反应？

AI 接线员监听的是系统收到的「播放 / 暂停」媒体键事件。如果耳机、转接头或外接按键本身不向 macOS 发送这个标准线控事件，软件也无法识别。首次引导里的「耳机播放键检测」可以先确认硬件是否支持；目前也不能改用键盘快捷键触发本地语音。

### 插上耳机后，为什么仍在用电脑麦克风？

AI 接线员使用 macOS 当前的系统输入设备，不会替你切换麦克风。请先到「系统设置 → 声音 → 输入」选择要使用的麦克风，再开始录音。

### 按键能触发，但没有识别出文字？

先确认「系统设置 → 隐私与安全性」中的麦克风权限已经打开，再检查系统输入设备是否选对、当前语音识别模型是否已经下载完整。Qwen3 和 SenseVoice 只会运行当前选中的那一档，不会在后台偷偷切换。

### 为什么文字没有落进当前输入框？

请确认辅助功能权限已打开，并在按下耳机键前把光标放进目标输入框。密码框等系统隐私区域会禁止合成输入。

### Intel Mac 或 Windows 可以使用本地语音吗？

不可以。v2 本地语音目前只支持 Apple 芯片 Mac；Windows 版目前提供耳机键映射，不包含本地语音识别和润色链路。

---

## 🧩 推荐搭配使用

- 🎙️ **[Typeless](https://www.typeless.com/?via=jonny)**：也可以让 AI 接线员只负责耳机键映射，由 Typeless 完成语音输入与整理。
- ☎️ **[Native Union 复古电话听筒](https://s.click.taobao.com/t?e=m%3D2%26s%3DwIrhGUcHebRw4vFB6t2Z2ueEDrYVVa6424t41fwIvtDRnNAdhLhtk430ZFekjizvxL1QHRKL8qiA5rFvxyOF9bwBMngvQdOvBQ7ZDE%2F6%2BIeRXNdY%2BjBkB5eYDQwFj6yC8kftjCRdkjx9Tuff7Tu%2FYJiUD8YEbaigH9ofgWr0NEuwIB%2BqTHya4ovEYPUpsQ%2FF2%2BRbLWXc2uZ5pCmcDdfSii21iRm1aQiYoGwWYjQ2nPkzWlhic%2BUoh0NEJ7GOeqvGxg5p7bh%2BFbQ%3D)**：有质感、完整功能经过验证，也最接近这个项目最初想保留的电话体验。

---

## 隐私说明

它需要「全局监听耳机键 + 合成键盘输入」两项敏感能力，所有代码均已开源，可供验证：

- 只响应耳机播放键，不读键盘、不记录内容；
- 文字走键盘事件写入，**不碰剪贴板**；
- 处理**全程本地**，联网仅发生在你点击下载模型时。

---

## 开发者信息

<details>
<summary><strong>从源码构建</strong></summary>

macOS（Xcode 命令行工具，Apple 芯片——内置 sherpa-onnx / llama.cpp 运行时为 arm64）：

```bash
./build.sh          # 编译 arm64 + 签名 → AI接线员.app
```

Windows（.NET 8 SDK）：

```bash
cd Commander-Windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

</details>

<details>
<summary><strong>第三方组件</strong></summary>

- 语音识别：[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) · Qwen3-ASR · SenseVoice · CT-Transformer 标点模型
- 本地大模型润色：[llama.cpp](https://github.com/ggml-org/llama.cpp) · Qwen3-8B
- Windows UI：WPF-UI · H.NotifyIcon.Wpf
- macOS 主体：SwiftUI · AppKit · CoreGraphics · AVFoundation

详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

</details>

## License

[MIT](LICENSE) © 2026 Jonny Yu
