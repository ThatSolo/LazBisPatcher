$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'LazBisPatcher.exe'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Run this next to the extracted LazBisPatcher.exe.' }
$desktop = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path $desktop 'LazBisPatcher.lnk'))
$link.TargetPath = $exe
$link.WorkingDirectory = $PSScriptRoot
$link.IconLocation = "$exe,0"
$link.Save()
