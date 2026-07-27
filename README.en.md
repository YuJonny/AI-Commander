<div align="center">

<img src="assets/logo.png" width="128" alt="AI 接线员 · AI Commander" />

# AI Commander · AI 接线员

**Press the play / pause button on your earphones to call your AI — speak, and the words land right at your cursor.**

**Speak casually, get clean prose** · Fully on-device · Free forever, made with love

[中文](README.md) · **English**

![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows-lightgrey)
[![Release](https://img.shields.io/github/v/release/YuJonny/AI-Commander)](https://github.com/YuJonny/AI-Commander/releases)
![On-device](https://img.shields.io/badge/speech%20processing-fully%20on--device-3b82f6)

<img src="assets/screenshot-v2.jpg" width="440" alt="AI Commander v2 main window" />

⭐ **If it helps, a Star means a lot.**

<sub>voice input · on-device speech recognition · local LLM · offline · media key remap · push-to-talk · vibe coding · macOS · Windows</sub>

</div>

---

## The Story

Making a phone call used to be a tangible thing.

The handset had weight; the cord curled around your hand. You dialed, then waited for someone at the other end to pick up. A telephone joined two people far apart, their voices carried through one thin line.

Then the telephone was folded into a thin sheet of glass. We stopped waiting by the phone for someone. The handset quietly left the stage and became a movie prop.

I've always loved that simple gesture: pick up the handset, the line connects, and you speak.

What if, in this day and age, such a telephone still existed?

Phones used to connect people to people.

Now, we use one to talk to the future.

---

## What It Does

AI Commander v2.1 has two independent modes. On an Apple-silicon Mac or Windows x64 PC, it can provide complete on-device voice input by itself; alternatively, it can act only as a headset-key mapper for an existing voice tool. The local voice mode does not require Typeless, WeType, or any other third-party input method.

- **One press to talk**: press the play / pause key to start, press again to stop — the words land right at your cursor.
- **Fully on-device, free forever**: recognition, cleanup and polish all run locally. Nothing is uploaded; the code is open source and auditable.
- **Two speech models**: Qwen3-ASR for high accuracy and mixed Chinese-English; SenseVoice for low memory.
- **Speak casually, get clean prose**: a rule layer strips fillers and fixes tech-term casing; an optional local LLM merges repetitions and normalizes punctuation and numbers — always faithful to what you said.
- **Works with your existing voice tools**: built-in presets for Doubao, WeType, Typeless, 闪电说, Wispr Flow, Superwhisper, AutoGLM, Claude Code and Codex, plus custom hotkeys.

### Native, Lightweight Design

The macOS app follows the Liquid Glass design language, while the Windows app uses Fluent 2. On both platforms, a floating capsule follows the flow — recording → transcribing → polishing — then slips away without interrupting your work.

## Models & Languages

| Component | Languages | Best at | Size |
|---|---|---|---|
| **Qwen3-ASR · High accuracy** (recommended) | 30 languages + 22 Chinese dialects | Mixed Chinese-English, long dictation; 16GB+ systems | ~840-990MB |
| **SenseVoice · Lightweight** | Chinese, English, Cantonese, Japanese, Korean | Everyday dictation; 8GB systems | ~220-290MB |
| **Qwen3-8B · Semantic polish** (optional) | Chinese-first, handles mixed Chinese-English | De-duplication, number formatting, sentence breaks | ~4.7GB |

Models are selected and tuned for real dictation scenarios with local data and open datasets.
If you only use key mapping, no model download is needed.

---

## Download & Install

Get the latest build from [Releases](https://github.com/YuJonny/AI-Commander/releases/latest).

| Platform | File | Capability |
|---|---|---|
| **macOS** (Apple silicon) | `AI.Commander_Mac_v2.1.dmg` | On-device voice input **＋** headset-key mapping |
| **Windows** (x64) | `AI.Commander_Win_v2.1.exe` | On-device voice input **＋** headset-key mapping |

### First launch on macOS

The app is not notarized, so the first launch may say "damaged" or "unidentified developer". The app is fine — pick either:

- **System Settings**: double-click the app → System Settings → Privacy & Security → click "Open Anyway".
- **Terminal fallback**:
  ```bash
  sudo xattr -cr /Applications/AI接线员.app
  ```

On first launch you'll be guided to grant **Accessibility** and **Microphone**, with an optional headset-key check.

### First launch on Windows

- SmartScreen: click "More info → Run anyway".
- Antivirus may flag the keyboard hook — add to the allowlist.
- Single portable file, x64, Windows 10/11, no admin rights or .NET required.
- If local voice input records silence, allow desktop apps to use the microphone in Windows Privacy settings.

## Common Questions

### Will the headset button trigger voice input and pause or resume music at the same time?

No. While AI Commander is enabled, it takes over and consumes the play / pause event. In local voice mode, the button controls recording only. In key-mapping mode, it sends only the selected tool's hotkey. It will not launch Typeless and pause music at the same time, nor will it automatically resume music when dictation ends.

This also means the headset button no longer controls media while AI Commander is enabled. Turn off the main switch and the listener is fully released, restoring the button's normal play / pause behavior. This is deliberate, exclusive key handling—not a double-trigger conflict.

### Will mapping the headset button to Fn for Typeless conflict with other shortcuts?

No. AI Commander maps Typeless's default `Fn` shortcut on macOS and `Right Alt` on Windows. It simulates that key only when you press the headset button and does not change the key's normal behavior. Use Custom only if you changed Typeless's default shortcut.

### Does WeType require an additional automation script?

No. On macOS, AI Commander maps WeType's `Fn + Space` hands-free mode: press once to start and once again to stop. On Windows, it maps the official hold-to-talk `Ctrl + Win` shortcut. Neither requires an additional automation script.

### Does first-time setup require a lot of configuration?

No. Open the app and choose an input mode; macOS guides you through the required permissions on first launch. Existing voice tools need no model download, while local voice input only adds one on-demand speech model.

---

## 🧩 Recommended Pairings

- 🎙️ **[Typeless](https://www.typeless.com/?via=jonny)**: you can also let AI Commander handle only the headset key, and leave voice input and cleanup to Typeless.
- ☎️ **[Native Union retro handset](https://s.click.taobao.com/t?e=m%3D2%26s%3DwIrhGUcHebRw4vFB6t2Z2ueEDrYVVa6424t41fwIvtDRnNAdhLhtk430ZFekjizvxL1QHRKL8qiA5rFvxyOF9bwBMngvQdOvBQ7ZDE%2F6%2BIeRXNdY%2BjBkB5eYDQwFj6yC8kftjCRdkjx9Tuff7Tu%2FYJiUD8YEbaigH9ofgWr0NEuwIB%2BqTHya4ovEYPUpsQ%2FF2%2BRbLWXc2uZ5pCmcDdfSii21iRm1aQiYoGwWYjQ2nPkzWlhic%2BUoh0NEJ7GOeqvGxg5p7bh%2BFbQ%3D)**: beautifully made, fully verified to work — and the closest thing to the telephone feeling this project set out to keep.

---

## Privacy

It needs two sensitive capabilities — global headset-key listening and synthetic keyboard input — so all the code is open source for you to verify:

- Responds only to the headset play key; never reads your keyboard or logs your content.
- Text is typed in via keyboard events — **the clipboard is never touched**.
- Everything runs on-device; the only network access is when you click to download a model.

---

## For Developers

<details>
<summary><strong>Build from source</strong></summary>

macOS (Xcode command-line tools, Apple silicon — bundled sherpa-onnx / llama.cpp runtimes are arm64):

```bash
./build.sh          # builds & signs arm64 → AI接线员.app
```

Windows (.NET 8 SDK):

```bash
cd Commander-Windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

</details>

<details>
<summary><strong>Third-party components</strong></summary>

- Speech recognition: [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) · Qwen3-ASR · SenseVoice · CT-Transformer punctuation
- Local LLM polish: [llama.cpp](https://github.com/ggml-org/llama.cpp) · Qwen3-8B
- Windows UI: WPF-UI · H.NotifyIcon.Wpf
- macOS: SwiftUI · AppKit · CoreGraphics · AVFoundation

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

</details>

## License

[MIT](LICENSE) © 2026 Jonny Yu
