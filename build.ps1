param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build src/Lantern.App/Lantern.App.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/Lantern.Tests/Lantern.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
    dotnet publish src/Lantern.App/Lantern.App.csproj -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o dist/Lantern
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item README.md dist/Lantern/README.md
    Write-Output ('Ready: ' + (Join-Path $PSScriptRoot 'dist\Lantern\Lantern.exe'))
} finally { Pop-Location }
