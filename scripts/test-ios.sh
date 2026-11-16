#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
xcodegen generate --spec ios/project.yml
dotnet build tests/MobileInteropFixture -c Release --nologo
dotnet tests/MobileInteropFixture/bin/Release/net10.0/MobileInteropFixture.dll > /tmp/lantern-ios-fixture.json 2> /tmp/lantern-ios-fixture.log &
FIXTURE_PID=$!
trap 'kill "$FIXTURE_PID" 2>/dev/null || true' EXIT
for attempt in {1..30}; do
  if test -s /tmp/lantern-ios-fixture.json; then break; fi
  sleep 1
done
python3 - <<'PY'
import json, pathlib, xml.etree.ElementTree as ET
pin = json.loads(pathlib.Path('/tmp/lantern-ios-fixture.json').read_text().splitlines()[0])['pc']
path = pathlib.Path('ios/Lantern.xcodeproj/xcshareddata/xcschemes/Lantern.xcscheme')
tree = ET.parse(path)
action = tree.getroot().find('TestAction')
action.set('shouldUseLaunchSchemeArgsEnv', 'NO')
variables = ET.SubElement(action, 'EnvironmentVariables')
ET.SubElement(variables, 'EnvironmentVariable', key='LANTERN_FIXTURE_PIN', value=pin, isEnabled='YES')
tree.write(path)
PY
DEVICE_ID=$(xcrun simctl list devices available -j | python3 -c 'import sys,json; d=json.load(sys.stdin); print(next(v["udid"] for group in d["devices"].values() for v in group if v["name"].startswith("iPhone")))')
xcodebuild -project ios/Lantern.xcodeproj -scheme Lantern -configuration Debug \
  -destination "platform=iOS Simulator,id=$DEVICE_ID" -derivedDataPath ios/build/simulator \
  -resultBundlePath ios/build/Tests.xcresult CODE_SIGNING_ALLOWED=NO \
  -parallel-testing-enabled NO test
xcrun simctl io "$DEVICE_ID" screenshot ios/build/ios-simulator.png
