$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$fixtureRoot = Join-Path $env:TEMP ('Lantern-update-test-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $env:LOCALAPPDATA ('LanternUpdates\test-' + [Guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $fixtureRoot 'installed'
New-Item -ItemType Directory -Path $fixtureRoot,$sourceRoot,$installRoot -Force | Out-Null
$source = @'
using System;
using System.IO;
public static class Program {
  public static void Main(string[] args) {
    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launched.txt"), args.Length == 2 && args[0] == "--data" ? "PASS" : "FAIL");
  }
}
'@
$codePath = Join-Path $fixtureRoot 'Fixture.cs'
[IO.File]::WriteAllText($codePath, $source)
$exe = Join-Path $sourceRoot 'Lantern.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe "/out:$exe" $codePath
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
[IO.File]::WriteAllText((Join-Path $installRoot 'Lantern.exe'), 'old fixture')
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$waiter = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep -Milliseconds 500' -WindowStyle Hidden -PassThru
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $projectRoot 'src/Lantern.App/UpdateInstaller.ps1') -TargetPid $waiter.Id -SourceDirectory $sourceRoot -InstallDirectory $installRoot -DataDirectory (Join-Path $fixtureRoot 'workspace with spaces') -ExpectedExeHash $hash
if ($LASTEXITCODE -ne 0) { throw 'Installer helper failed.' }
$deadline = (Get-Date).AddSeconds(10)
while (-not (Test-Path -LiteralPath (Join-Path $installRoot 'launched.txt'))) { if ((Get-Date) -gt $deadline) { throw 'Updated executable did not start.' }; Start-Sleep -Milliseconds 100 }
if ((Get-FileHash -LiteralPath (Join-Path $installRoot 'Lantern.exe') -Algorithm SHA256).Hash -ne $hash) { throw 'Installed hash mismatch.' }
if ([IO.File]::ReadAllText((Join-Path $sourceRoot 'backup\Lantern.exe')) -ne 'old fixture') { throw 'Backup missing.' }
if ([IO.File]::ReadAllText((Join-Path $installRoot 'launched.txt')) -ne 'PASS') { throw 'Restart arguments did not preserve the workspace path.' }
Write-Output 'PASS: Windows PowerShell installer waits, backs up, replaces and restarts with workspace arguments.'
