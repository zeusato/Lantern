param([string]$DotNet = 'dotnet', [string]$Node = 'node')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
$fixture = $null
try {
    & $Node --test pwa/tests/model.test.mjs pwa/tests/pc.test.mjs pwa/tests/qr-scanner.test.mjs pwa/tests/calculator.test.mjs pwa/tests/calculator-lock.test.mjs pwa/tests/camouflage.test.mjs relay/tests/relay.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Web unit/relay checks failed.' }
    & $DotNet build tests/WebInteropFixture -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Web fixture build failed.' }
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Lantern-Web-QA-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $metadata = Join-Path $testRoot 'fixture.json'
    $fixtureDll = Join-Path $projectRoot 'tests/WebInteropFixture/bin/Release/net10.0-windows/WebInteropFixture.dll'
    $fixture = Start-Process -FilePath $DotNet -ArgumentList @(('"' + $fixtureDll + '"'), ('"' + (Join-Path $testRoot 'workspace') + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput $metadata -RedirectStandardError (Join-Path $testRoot 'fixture.log')
    $ready = $false
    for ($attempt=0; $attempt -lt 80; $attempt++) {
        if ($fixture.HasExited) { throw 'Web fixture exited before readiness.' }
        try { $null = Get-Content -LiteralPath $metadata -Raw | ConvertFrom-Json; if ((Get-Item -LiteralPath $metadata).Length -gt 20) { $ready=$true; break } } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Web fixture did not become ready.' }
    & $Node pwa/tests/interop.mjs $metadata
    if ($LASTEXITCODE -ne 0) { throw 'Browser/.NET encryption and mobile protocol checks failed.' }
} finally { if ($fixture -and -not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit() }; Pop-Location }
