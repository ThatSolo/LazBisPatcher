param(
    [string]$Executable = (Join-Path $PSScriptRoot '../out/release-0.1.5/LazBisPatcher.exe'),
    [string]$Version = '0.1.5'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root = Join-Path ([IO.Path]::GetTempPath()) ('LazBisPortable-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$app = Join-Path $root 'LazBisPatcher.exe'
Copy-Item -LiteralPath $Executable -Destination $app
if (@(Get-ChildItem -LiteralPath $root -Force).Count -ne 1) { throw 'Fresh fixture must contain only the app EXE' }
$startup = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process -FilePath $app -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) { throw "Portable app exited: $($process.ExitCode)" }
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'Portable window did not open' }
    $startup.Stop()
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $elements = $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    $names = @($elements | ForEach-Object { $_.Current.Name })
    if ('LazBisPatcher' -notin @($names | ForEach-Object { $_.Trim() }) -or !($names | Where-Object { $_ -like "Patcher v$Version*" })) {
        throw 'Portable title or footer differs'
    }
    if ((Test-Path (Join-Path $root 'LazBisPatcher.Helper.exe')) -or
        (Test-Path (Join-Path $root '.lazbis-self-update/LazBisPatcher.Helper.exe'))) { throw 'Normal launch extracted an unnecessary helper' }
    if (!($names | Where-Object { $_ -like 'Browse*' }) -or !('Check for Updates' -in $names)) { throw 'Main screen controls missing' }
    # Observe startup after the first window, including any automatically opened dialog.
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $elements = $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
        if (@($elements | Where-Object { $_.Current.Name -match '(?i)notices' }).Count) { throw 'Notices control remains' }
        $windows = $window.FindAll([Windows.Automation.TreeScope]::Subtree,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Window))
        if ($windows.Count -ne 1 -or !$window.Current.IsEnabled) { throw 'An unexpected popup blocks the main screen' }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    $closeDeadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $null = $process.CloseMainWindow()
        if ($process.WaitForExit(250)) { break }
    } while ([DateTime]::UtcNow -lt $closeDeadline)
    if (!$process.HasExited) { throw 'Portable app did not close' }
    Write-Host "PASS single-EXE Windows launch directly to main screen, LazBisPatcher title, v$Version footer, no Notices control or popup, normal exit; no helper extracted"
    Write-Host "First window observed after $($startup.ElapsedMilliseconds) ms (single sample, 200 ms polling)"
    Write-Host "Fixture retained at $root"
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id }
    $process.Dispose()
}
