#!/bin/bash
# Re-sign AI接线员.app with a STABLE identity so the Accessibility/麦克风 grants
# survive rebuilds. macOS/TCC tracks grants by the code's *designated requirement*;
# a real cert (Apple Development) keeps that requirement constant across rebuilds,
# whereas ad-hoc keys on the per-build cdhash (grant breaks every rebuild).
set -e
APP="${1:-$(cd "$(dirname "$0")" && pwd)/AI接线员.app}"
ENT="$(dirname "$0")/KeyRelay/KeyRelay.entitlements"

# Apple Development 证书（本机已有）。换机器请改成你自己的：
#   security find-identity -v -p codesigning
DEV_ID="67926F55C3809076A19F6260FD3C567970BCC4D8"
if security find-identity -v -p codesigning 2>/dev/null | grep -q "$DEV_ID"; then
    SIGN="$DEV_ID"
    echo "Signing with Apple Development cert ($DEV_ID) → 授权可跨重编译保留"
else
    SIGN="-"
    echo "⚠️ 未找到开发证书 → 回退 ad-hoc（每次重编译需重新授权）"
fi

echo "Signing: $APP"
xattr -cr "$APP"
codesign --force --deep \
         --identifier com.vvclaw.commander \
         --entitlements "$ENT" \
         --sign "$SIGN" "$APP"
codesign --verify --verbose "$APP" && echo "✅ signed & valid"
codesign -dvvv "$APP" 2>&1 | grep -E "CDHash=|Authority=" | head -3