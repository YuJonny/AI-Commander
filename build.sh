#!/bin/bash
# Build AI接线员.app — a universal (Intel + Apple Silicon) macOS app bundle,
# then sign it with a stable ad-hoc identity (via sign.sh) so the macOS
# Accessibility (辅助功能) grant survives rebuilds.
#
# Requirements: Xcode command-line tools (swiftc, lipo, codesign).
# Usage: ./build.sh
set -e
cd "$(dirname "$0")"

APP="AI接线员.app"
EXEC="Commander"
SDK="$(xcrun --show-sdk-path)"

compile() {  # $1 = arch target
    swiftc -O -sdk "$SDK" -parse-as-library \
        -framework SwiftUI -framework AppKit -framework CoreGraphics \
        -framework IOKit -framework Carbon -framework ApplicationServices \
        -framework MediaPlayer \
        KeyRelay/*.swift -o "/tmp/${EXEC}_$1" -target "$1"
}

echo "==> Compiling arm64 ..."
compile arm64-apple-macosx13.0
echo "==> Compiling x86_64 ..."
compile x86_64-apple-macosx13.0

echo "==> Creating universal binary ..."
lipo -create "/tmp/${EXEC}_arm64-apple-macosx13.0" "/tmp/${EXEC}_x86_64-apple-macosx13.0" \
     -output "/tmp/${EXEC}_universal"
rm -f "/tmp/${EXEC}_arm64-apple-macosx13.0" "/tmp/${EXEC}_x86_64-apple-macosx13.0"

echo "==> Assembling ${APP} ..."
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" \
         "$APP/Contents/Resources/zh-Hans.lproj" \
         "$APP/Contents/Resources/en.lproj"
mv "/tmp/${EXEC}_universal"                    "$APP/Contents/MacOS/$EXEC"
cp KeyRelay/Info.plist                         "$APP/Contents/Info.plist"
cp Commander.icns                              "$APP/Contents/Resources/"
cp statusbar_icon.png statusbar_icon@2x.png    "$APP/Contents/Resources/"
cp KeyRelay/zh-Hans.lproj/Localizable.strings  "$APP/Contents/Resources/zh-Hans.lproj/"
cp KeyRelay/en.lproj/Localizable.strings       "$APP/Contents/Resources/en.lproj/"

echo "==> Signing (stable identity) ..."
bash sign.sh "$PWD/$APP"

echo ""
echo "✅ Done: $APP  (universal, signed)"
