#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
command -v xcodegen >/dev/null || { echo 'Install XcodeGen on the macOS build host first.' >&2; exit 1; }
xcodegen generate --spec ios/project.yml
xcodebuild -project ios/Lantern.xcodeproj -scheme Lantern -configuration Release -sdk iphoneos \
  -destination 'generic/platform=iOS' -derivedDataPath ios/build/device \
  CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY= build
mkdir -p ios/build/package/Payload
ditto ios/build/device/Build/Products/Release-iphoneos/Lantern.app ios/build/package/Payload/Lantern.app
# This device binary intentionally has no Apple signature or provisioning profile. Scarlet signs it.
test -f ios/build/package/Payload/Lantern.app/Lantern
test ! -e ios/build/package/Payload/Lantern.app/embedded.mobileprovision
test ! -e ios/build/package/Payload/Lantern.app/Simulator.entitlements
file ios/build/package/Payload/Lantern.app/Lantern | grep -q arm64
if strings ios/build/package/Payload/Lantern.app/Lantern | grep -F 'LANTERN_UI_FIXTURE_PIN'; then
  echo 'Simulator fixture configuration leaked into Release.' >&2
  exit 1
fi
mkdir -p ios/output
rm -f ios/output/Lantern-iOS-unsigned.ipa
(cd ios/build/package && /usr/bin/zip -qry ../../output/Lantern-iOS-unsigned.ipa Payload)
(cd ios/output && shasum -a 256 Lantern-iOS-unsigned.ipa > IOS-SHA256SUMS.txt)
unzip -t ios/output/Lantern-iOS-unsigned.ipa
