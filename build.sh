#!/bin/bash
# Build AI接线员.app (v2.0) — Apple Silicon (arm64) macOS app with on-device voice
# input (sherpa-onnx ASR). sign.sh uses a stable Apple Development identity when
# available so Accessibility/Microphone grants survive rebuilds, with ad-hoc fallback.
#
# NOTE: v2.0 是 arm64-only —— 本地语音用的 sherpa-onnx 库为 arm64。
# Requirements: Xcode command-line tools.
# Usage: ./build.sh
set -e
cd "$(dirname "$0")"

APP="AI接线员.app"
EXEC="Commander"
SDK="$(xcrun --show-sdk-path)"
LIBDIR="vendor/sherpa-onnx/lib"

echo "==> Compiling (arm64) with voice engine ..."
swiftc -O -sdk "$SDK" -parse-as-library \
    -framework SwiftUI -framework AppKit -framework CoreGraphics \
    -framework IOKit -framework Carbon -framework ApplicationServices \
    -framework MediaPlayer -framework AVFoundation \
    -import-objc-header KeyRelay/sherpa-c-api.h \
    -L "$LIBDIR" -lsherpa-onnx-c-api \
    -Xlinker -rpath -Xlinker @executable_path/../Frameworks \
    KeyRelay/*.swift -o "/tmp/${EXEC}_arm64" -target arm64-apple-macosx13.0

echo "==> Assembling ${APP} ..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" \
         "$APP/Contents/Frameworks" \
         "$APP/Contents/Resources/zh-Hans.lproj" \
         "$APP/Contents/Resources/en.lproj"
mv "/tmp/${EXEC}_arm64"                          "$APP/Contents/MacOS/$EXEC"
cp KeyRelay/Info.plist                           "$APP/Contents/Info.plist"
cp Commander.icns                                "$APP/Contents/Resources/"
cp statusbar_icon.png statusbar_icon@2x.png      "$APP/Contents/Resources/"
cp KeyRelay/zh-Hans.lproj/Localizable.strings    "$APP/Contents/Resources/zh-Hans.lproj/"
cp KeyRelay/en.lproj/Localizable.strings         "$APP/Contents/Resources/en.lproj/"
cp -R KeyRelay/ToolIcons                         "$APP/Contents/Resources/ToolIcons"

echo "==> Bundling sherpa-onnx dylibs into Contents/Frameworks ..."
cp "$LIBDIR/libsherpa-onnx-c-api.dylib"   "$APP/Contents/Frameworks/"
cp "$LIBDIR/libonnxruntime.1.24.4.dylib"  "$APP/Contents/Frameworks/"

echo "==> Bundling llama.cpp runtime (LLM 增强, rpath=@loader_path 同目录即可) ..."
LLAMA="vendor/llama.cpp/llama-b9873"
mkdir -p "$APP/Contents/Frameworks/llama"
cp    "$LLAMA/llama-server"                     "$APP/Contents/Frameworks/llama/"
cp -L "$LLAMA/libllama-server-impl.dylib" \
      "$LLAMA/libllama-common.0.dylib" \
      "$LLAMA/libmtmd.0.dylib" \
      "$LLAMA/libllama.0.dylib" \
      "$LLAMA/libggml.0.dylib" \
      "$LLAMA/libggml-cpu.0.dylib" \
      "$LLAMA/libggml-blas.0.dylib" \
      "$LLAMA/libggml-metal.0.dylib" \
      "$LLAMA/libggml-rpc.0.dylib" \
      "$LLAMA/libggml-base.0.dylib"             "$APP/Contents/Frameworks/llama/"

echo "==> Signing (stable identity) ..."
bash sign.sh "$PWD/$APP"

echo ""
echo "✅ Done: $APP  (arm64, voice-enabled, signed)"
