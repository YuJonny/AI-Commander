#!/bin/bash
set -e

APP_NAME="接线员"
EXECUTABLE="KeyRelay"
BUNDLE="${APP_NAME}.app"
SDK_PATH=$(xcrun --show-sdk-path)

ARCHS=""
if [[ $(uname -m) == "arm64" ]]; then
    ARCHS="-target arm64-apple-macosx12.0"
else
    ARCHS="-target x86_64-apple-macosx12.0"
fi

# Build for both architectures (universal binary)
if [ "$1" == "universal" ]; then
    echo "Building universal binary..."

    swiftc $ARCHS -sdk "$SDK_PATH" -parse-as-library \
        -framework SwiftUI -framework AppKit -framework CoreGraphics \
        -framework IOKit -framework Carbon -framework ApplicationServices \
        KeyRelay/*.swift -o /tmp/KeyRelay_arm64 \
        -target arm64-apple-macosx12.0

    swiftc -sdk "$SDK_PATH" -parse-as-library \
        -framework SwiftUI -framework AppKit -framework CoreGraphics \
        -framework IOKit -framework Carbon -framework ApplicationServices \
        KeyRelay/*.swift -o /tmp/KeyRelay_x86_64 \
        -target x86_64-apple-macosx12.0

    lipo -create /tmp/KeyRelay_arm64 /tmp/KeyRelay_x86_64 -output /tmp/KeyRelay_universal
    rm /tmp/KeyRelay_arm64 /tmp/KeyRelay_x86_64
    BINARY="/tmp/KeyRelay_universal"
else
    echo "Building for current architecture..."
    swiftc $ARCHS -sdk "$SDK_PATH" -parse-as-library \
        -framework SwiftUI -framework AppKit -framework CoreGraphics \
        -framework IOKit -framework Carbon -framework ApplicationServices \
        KeyRelay/*.swift -o /tmp/KeyRelay_build
    BINARY="/tmp/KeyRelay_build"
fi

# Create .app bundle
rm -rf "${BUNDLE}"
mkdir -p "${BUNDLE}/Contents/MacOS"
mkdir -p "${BUNDLE}/Contents/Resources"
cp "$BINARY" "${BUNDLE}/Contents/MacOS/${EXECUTABLE}"
cp KeyRelay/Info.plist "${BUNDLE}/Contents/"
rm "$BINARY"

echo "✅ Build complete: ${BUNDLE}"
echo "   Run: open \"${BUNDLE}\""
