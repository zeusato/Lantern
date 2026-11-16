#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
xcodegen generate --spec ios/project.yml
dotnet build tests/MobileInteropFixture -c Release --nologo
dotnet tests/MobileInteropFixture/bin/Release/net10.0/MobileInteropFixture.dll > /tmp/lantern-ios-fixture.json 2> /tmp/lantern-ios-fixture.log &
FIXTURE_PID=$!
RELAY_PID=''
trap 'kill "$FIXTURE_PID" ${RELAY_PID:+"$RELAY_PID"} 2>/dev/null || true' EXIT
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
defaults write com.apple.iphonesimulator ConnectHardwareKeyboard -bool NO
xcrun simctl boot "$DEVICE_ID" || true
xcrun simctl bootstatus "$DEVICE_ID" -b
OPENSSL_BIN="$(brew --prefix openssl@3)/bin/openssl"
"$OPENSSL_BIN" version
"$OPENSSL_BIN" req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -sha256 -nodes -keyout /tmp/lantern-ios-root.key -out /tmp/lantern-ios-root.crt -days 1 \
  -subj '/CN=Lantern Simulator Root' -addext 'basicConstraints=critical,CA:TRUE' -addext 'keyUsage=critical,keyCertSign,cRLSign'
"$OPENSSL_BIN" req -newkey ec -pkeyopt ec_paramgen_curve:P-256 -sha256 -nodes -keyout /tmp/lantern-ios-relay.key -out /tmp/lantern-ios-relay.csr -subj '/CN=localhost'
printf '%s\n' 'basicConstraints=critical,CA:FALSE' 'keyUsage=critical,digitalSignature' 'extendedKeyUsage=serverAuth' 'subjectAltName=IP:127.0.0.1,DNS:localhost' > /tmp/lantern-ios-relay.ext
"$OPENSSL_BIN" x509 -req -in /tmp/lantern-ios-relay.csr -CA /tmp/lantern-ios-root.crt -CAkey /tmp/lantern-ios-root.key -CAcreateserial \
  -out /tmp/lantern-ios-relay.crt -days 1 -sha256 -extfile /tmp/lantern-ios-relay.ext
"$OPENSSL_BIN" verify -CAfile /tmp/lantern-ios-root.crt -purpose sslserver -verify_ip 127.0.0.1 /tmp/lantern-ios-relay.crt
"$OPENSSL_BIN" x509 -in /tmp/lantern-ios-root.crt -outform DER -out /tmp/lantern-ios-root.der
python3 - <<'PY'
import base64, pathlib, xml.etree.ElementTree as ET
path = pathlib.Path('ios/Lantern.xcodeproj/xcshareddata/xcschemes/Lantern.xcscheme')
tree = ET.parse(path)
variables = tree.getroot().find('TestAction/EnvironmentVariables')
root = base64.b64encode(pathlib.Path('/tmp/lantern-ios-root.der').read_bytes()).decode()
ET.SubElement(variables, 'EnvironmentVariable', key='LANTERN_FIXTURE_CA', value=root, isEnabled='YES')
tree.write(path)
PY
node tests/MobileInteropFixture/relay-harness.mjs /tmp/lantern-ios-relay.crt /tmp/lantern-ios-relay.key > /tmp/lantern-ios-relay.log 2>&1 &
RELAY_PID=$!
xcodebuild -project ios/Lantern.xcodeproj -scheme Lantern -configuration Debug \
  -destination "platform=iOS Simulator,id=$DEVICE_ID" -derivedDataPath ios/build/simulator \
  -resultBundlePath ios/build/Tests.xcresult CODE_SIGNING_ALLOWED=YES CODE_SIGN_IDENTITY=- \
  -parallel-testing-enabled NO -test-timeouts-enabled YES \
  -default-test-execution-time-allowance 120 -maximum-test-execution-time-allowance 180 test
xcrun simctl io "$DEVICE_ID" screenshot ios/build/ios-simulator.png
