param(
    [string]$Output = (Join-Path $PSScriptRoot '../out'),
    [string]$LegacyZip = (Join-Path $PSScriptRoot '../out/lazbis-1.0/lazbis.zip')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root = Join-Path ([IO.Path]::GetTempPath()) ('LazBisManualUi-' + [Guid]::NewGuid().ToString('N'))
$exe = [IO.Path]::GetFullPath((Join-Path $Output 'fixture-0.1.5/LazBisPatcher.exe'))
& dotnet run --project (Join-Path $PSScriptRoot '../tests/LazBisPatcher.Smoke') -c Release -- --prepare-lazbis-ui $LegacyZip $root
if ($LASTEXITCODE -ne 0) { throw 'UI fixture preparation failed' }

function Get-Snapshot([string]$Folder) {
    $result = @{}
    Get-ChildItem -LiteralPath $Folder -File -Recurse | ForEach-Object {
        $result[[IO.Path]::GetRelativePath($Folder, $_.FullName)] = (Get-FileHash -LiteralPath $_.FullName).Hash
    }
    return $result
}
function Assert-Same($Before, $After) {
    if ($Before.Count -ne $After.Count) { throw 'Fixture file paths changed unexpectedly' }
    foreach ($path in $Before.Keys) { if ($Before[$path] -ne $After[$path]) { throw "Fixture bytes changed: $path" } }
}
function Get-Window([Diagnostics.Process]$Process, [string]$Title) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        if ($Process.HasExited) { throw 'Fixture app exited' }
        $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id))
        foreach ($window in $windows) {
            $owned = $window.FindAll([Windows.Automation.TreeScope]::Subtree,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Window))
            foreach ($item in $owned) { if ($item.Current.Name -eq $Title) { return $item } }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    $observed = ($windows | ForEach-Object {
        $_.FindAll([Windows.Automation.TreeScope]::Subtree,[Windows.Automation.Condition]::TrueCondition) |
            ForEach-Object { $_.Current.Name }
    }) -join ' | '
    throw "Fixture window missing: $Title. Observed: $observed"
}
function Get-Control($Window, [string]$Id) {
    return $Window.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
}
function Invoke-Control($Window, [string]$Id) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $control = Get-Control $Window $Id
        if ($control -and $control.Current.IsEnabled -and !$control.Current.IsOffscreen) {
            ([Windows.Automation.InvokePattern]$control.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Fixture control unavailable: $Id"
}
function Get-Text($Window) {
    return ($Window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name }) -join "`n"
}
function Wait-Idle($Window) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $check = Get-Control $Window 'CheckButton'
        if ($check -and $check.Current.IsEnabled) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Fixture operation did not finish'
}

foreach ($case in 'official','fork') {
    $folder = Join-Path $root $case
    $app = Join-Path $folder 'LazBisPatcher.exe'
    Copy-Item -LiteralPath $exe -Destination $app
    $mq = Join-Path $folder 'MacroQuest'
    $scriptFolder = Join-Path $mq 'lua/lazbis'
    $before = Get-Snapshot $mq
    $scripts = Get-Snapshot $scriptFolder
    $player = @{}
    foreach ($relative in 'config/lazbis_lockouts.lua','resources/lazbis.db','resources/lazbis.db-wal','resources/lazbis.db-shm','lua/other-tool.lua') {
        $player[$relative] = (Get-FileHash -LiteralPath (Join-Path $mq $relative)).Hash
    }
    $process = Start-Process -FilePath $app -PassThru
    try {
        $main = Get-Window $process 'LazBis Patcher'
        Invoke-Control $main 'ActionButton'
        $title = if ($case -eq 'official') { 'Update LazBis' } else { 'Existing LazBis copy found' }
        $review = Get-Window $process $title
        $text = Get-Text $review
        if ($text -notlike '*MacroQuest can stay open*' -or $text -notlike '*/lua stop lazbis*' -or $text -notlike '*/lua stop lazbis/bg*') { throw 'Stop-script guidance missing' }
        if ($case -eq 'official' -and $text -notlike '*Official 1.0 files recognized*') { throw 'Official upgrade recognition missing' }
        if ($case -eq 'fork') {
            $confirm = Get-Control $review 'ConfirmButton'
            if ($confirm -and !$confirm.Current.IsOffscreen) { throw 'Unknown fork exposes Apply' }
            if ($text -notlike '*resources\lazbis.db*' -or $text -notlike '*config\lazbis_lockouts.lua*' -or $text -notlike '*separate MacroQuest test copy*') { throw 'Fork switch/data guidance missing' }
        }
        Invoke-Control $review 'DismissButton'
        Wait-Idle $main
        Assert-Same $before (Get-Snapshot $mq)
        if (Test-Path (Join-Path $mq 'lua/.lazbis-patcher/installed.json')) { throw 'Dismissed preview recorded an installation' }
        if ($case -eq 'fork') {
            $moved = [IO.Path]::GetFullPath((Join-Path $root 'fork-backup-outside-lua'))
            if (!$moved.StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                !$scriptFolder.StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture move escaped its root' }
            Move-Item -LiteralPath $scriptFolder -Destination $moved
            Invoke-Control $main 'CheckButton'
            Invoke-Control $main 'ActionButton'
            $install = Get-Window $process 'Install LazBis'
            Invoke-Control $install 'ConfirmButton'
            $state = Join-Path $mq 'lua/.lazbis-patcher/installed.json'
            $deadline = [DateTime]::UtcNow.AddSeconds(45)
            while (!(Test-Path $state) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
            if (!(Test-Path $state)) { throw 'Clean install was not committed' }
            $record = Get-Content -LiteralPath $state -Raw | ConvertFrom-Json
            if ($record.release.version -ne '1.0.1') { throw 'Wrong clean install version' }
            $expected = Get-Content -LiteralPath (Join-Path $root 'expected-managed.json') -Raw | ConvertFrom-Json
            foreach ($entry in $expected.PSObject.Properties) {
                if ((Get-FileHash -LiteralPath (Join-Path $scriptFolder $entry.Name)).Hash.ToLowerInvariant() -ne $entry.Value) { throw 'Clean installed bytes differ' }
            }
            Assert-Same $scripts (Get-Snapshot $moved)
            foreach ($relative in $player.Keys) { if ((Get-FileHash -LiteralPath (Join-Path $mq $relative)).Hash -ne $player[$relative]) { throw "Player data changed: $relative" } }
            Wait-Idle $main
            if (Test-Path (Join-Path $mq 'lua/.lazbis-patcher/pending.json')) { throw 'Clean install left pending recovery' }
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        do { $null = $process.CloseMainWindow(); if ($process.WaitForExit(250)) { break } } while ([DateTime]::UtcNow -lt $deadline)
        if (!$process.HasExited) { throw 'Fixture app did not close normally' }
        Write-Host "PASS native $case preview, stop-script guidance, dismissed plan unchanged$(if ($case -eq 'fork') { ', blocked in place, manual move and clean install, old fork and shared data preserved' })"
    } finally {
        $process.Refresh()
        if (!$process.HasExited) { Stop-Process -Id $process.Id }
        $process.Dispose()
    }
}
Write-Host "LazBis UI fixtures retained at $root"
