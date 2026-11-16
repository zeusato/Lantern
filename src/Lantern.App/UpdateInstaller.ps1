param([Parameter(Mandatory)][int]$TargetPid, [Parameter(Mandatory)][string]$SourceDirectory, [Parameter(Mandatory)][string]$InstallDirectory, [Parameter(Mandatory)][string]$DataDirectory, [Parameter(Mandatory)][string]$ExpectedExeHash)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)
$installRoot = [IO.Path]::GetFullPath($InstallDirectory)
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'LanternUpdates')) + [IO.Path]::DirectorySeparatorChar
if (-not $sourceRoot.StartsWith($cacheRoot, [StringComparison]::OrdinalIgnoreCase) -or $installRoot.TrimEnd('\') -eq [IO.Path]::GetPathRoot($installRoot).TrimEnd('\')) { throw 'Invalid update location.' }
$allowed = @('Lantern.exe','HUONG-DAN.md','THIRD-PARTY-NOTICES.md','DOTNET-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt','WPF-THIRD-PARTY-NOTICES.txt')
$sourceExe = Join-Path $sourceRoot 'Lantern.exe'
if ((Get-FileHash -LiteralPath $sourceExe -Algorithm SHA256).Hash -ne $ExpectedExeHash) { throw 'Update hash changed.' }
$files = @(Get-ChildItem -LiteralPath $sourceRoot -File)
if (@($files | Where-Object { $_.Name -notin $allowed }).Count -gt 0) { throw 'Unexpected update file.' }
$backup = Join-Path $sourceRoot 'backup'
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$replaced = [Collections.Generic.List[string]]::new()
try {
    $target = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
    if ($target) { $target.WaitForExit(120000) | Out-Null; if (-not $target.HasExited) { throw 'Lantern did not exit.' } }
    foreach ($file in $files) {
        $destination = Join-Path $installRoot $file.Name
        if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backup $file.Name) }
        $replaced.Add($file.Name)
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    Start-Process -FilePath (Join-Path $installRoot 'Lantern.exe') -ArgumentList @('--data', ('"' + $DataDirectory + '"')) -WorkingDirectory $installRoot -WindowStyle Normal
    'Updated successfully.' | Set-Content -LiteralPath (Join-Path $sourceRoot 'result.txt')
} catch {
    foreach ($name in $replaced) {
        $saved = Join-Path $backup $name
        if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination (Join-Path $installRoot $name) -Force }
        else { Remove-Item -LiteralPath (Join-Path $installRoot $name) -ErrorAction SilentlyContinue }
    }
    $_.Exception.Message | Set-Content -LiteralPath (Join-Path $sourceRoot 'result.txt')
    Add-Type -AssemblyName PresentationFramework
    [Windows.MessageBox]::Show('Không cài được bản cập nhật. Bản cũ đã được giữ lại. Hãy mở Lantern và thử lại. Chi tiết: ' + $_.Exception.Message, 'Lantern') | Out-Null
}
