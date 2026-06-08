#!/bin/bash
# Re-sign AI接线员.app with a STABLE ad-hoc identity.
# Run this after every rebuild so the Accessibility/辅助功能 grant survives
# (macOS tracks the grant by code-signature hash — a stable hash = grant persists).
set -e
APP="${1:-/Users/vvclaw/Downloads/接线员/AI接线员.app}"
ENT="$(dirname "$0")/KeyRelay/KeyRelay.entitlements"
echo "Signing: $APP"
xattr -cr "$APP"
codesign --force --deep \
         --identifier com.vvclaw.commander \
         --entitlements "$ENT" \
         --sign - "$APP"
codesign --verify --verbose "$APP" && echo "✅ signed & valid"
codesign -dvvv "$APP" 2>&1 | grep "CDHash=" | head -1
