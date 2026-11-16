param([string]$Version = '1.2.3', [string]$OutputDirectory = 'release-output', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a stable version such as 1.1.0.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
if (-not $outputRoot.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be inside the project.' }
$payload = Join-Path $outputRoot 'portable'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Push-Location $projectRoot
try {
    if (-not $SkipTests) {
        dotnet run --project tests/Lantern.Tests/Lantern.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed.' }
        & (Join-Path $PSScriptRoot 'Test-UpdateInstaller.ps1')
    }
    dotnet publish src/Lantern.App/Lantern.App.csproj -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md -Destination (Join-Path $payload 'HUONG-DAN.md')
    Copy-Item -LiteralPath THIRD-PARTY-NOTICES.md -Destination $payload
    Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses/DOTNET-LICENSE.txt'), (Join-Path $projectRoot 'licenses/DOTNET-THIRD-PARTY-NOTICES.txt'), (Join-Path $projectRoot 'licenses/WPF-THIRD-PARTY-NOTICES.txt') -Destination $payload
    $archive = Join-Path $outputRoot 'Lantern-Windows-x64.zip'
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
    Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $payload -File | Where-Object Name -in @('Lantern.exe','HUONG-DAN.md','THIRD-PARTY-NOTICES.md','DOTNET-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt','WPF-THIRD-PARTY-NOTICES.txt') | Select-Object -ExpandProperty FullName) -DestinationPath $archive -CompressionLevel Optimal
    $checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  Lantern-Windows-x64.zip'
    [IO.File]::WriteAllText((Join-Path $outputRoot 'SHA256SUMS.txt'), $checksum + [Environment]::NewLine)
    Write-Output "Ready: $archive"
} finally { Pop-Location }
