param(
    [string]$Output = (Join-Path $PSScriptRoot '../out'),
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$SourceVersion = '0.1.5',
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$TargetVersion = '0.1.6',
    [ValidateSet('success','failed-launch','interrupted','download-interrupted')]
    [string[]]$Cases = @('success','failed-launch','interrupted','download-interrupted'),
    [switch]$ExpectStagingBlock
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class LazBisHandoffDialog {
    [DllImport("user32.dll")] static extern IntPtr GetLastActivePopup(IntPtr owner);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    public static bool Confirm(IntPtr owner, int process) {
        var dialog=GetLastActivePopup(owner);
        if(dialog==IntPtr.Zero || dialog==owner || !IsWindowEnabled(dialog)) return false;
        GetWindowThreadProcessId(dialog, out var actual);
        var title=new StringBuilder(256);
        GetWindowText(dialog,title,title.Capacity);
        if(actual!=process || title.ToString()!="Update LazBisPatcher") return false;
        var yes=GetDlgItem(dialog,6);
        return yes!=IntPtr.Zero && IsWindowEnabled(yes) && PostMessage(dialog,0x0111,new IntPtr(6),yes);
    }
}
'@
$Output = [IO.Path]::GetFullPath($Output)
$old = Join-Path $Output "fixture-$SourceVersion"
$new = Join-Path $Output "fixture-$TargetVersion/LazBisPatcher.exe"
if ([version]$TargetVersion -le [version]$SourceVersion) { throw 'Target must be newer than source.' }
foreach ($inputExe in @((Join-Path $old 'LazBisPatcher.exe'), $new)) {
    $expectedVersion = if ($inputExe -eq $new) { $TargetVersion } else { $SourceVersion }
    $info = (Get-Item -LiteralPath $inputExe).VersionInfo
    if ("$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)" -ne $expectedVersion) {
        throw "Unexpected executable version: $inputExe"
    }
}
$root = Join-Path ([IO.Path]::GetTempPath()) ("LazBisHandoff-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$fixtureRoot = [IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar

function Get-OwnedProcesses([string]$Folder) {
    $paths = @((Join-Path $Folder 'LazBisPatcher.exe'), (Join-Path $Folder 'LazBisPatcher.Helper.exe'),
        (Join-Path $Folder '.lazbis-self-update/LazBisPatcher.Helper.exe'))
    Get-Process LazBisPatcher,LazBisPatcher.Helper -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -in $paths }
}

function Invoke-UpdateButton([Diagnostics.Process]$Process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $button = $null
    do {
        $Process.Refresh()
        if ($Process.HasExited) { throw 'Source app exited before Update Patcher was clicked' }
        if ($Process.MainWindowHandle -ne 0) {
            $window = [Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
            $button = $window.FindFirst([Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Update Patcher'))
            if ($button -and $button.Current.IsEnabled -and !$button.Current.IsOffscreen) { break }
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$button -or !$button.Current.IsEnabled -or $button.Current.IsOffscreen) { throw 'Update Patcher button did not become available' }
    $owner = $Process.MainWindowHandle
    ([Windows.Automation.InvokePattern]$button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    # The classic confirmation dialog may lack UIA Invoke support on this Windows host.
    # Send IDYES only to this test process's owned, enabled update-confirmation dialog.
    $confirmed = $false
    do {
        $confirmed = [LazBisHandoffDialog]::Confirm($owner, $Process.Id)
        if ($confirmed) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$confirmed) { throw 'Patcher update confirmation did not appear' }
}

foreach ($case in $Cases) {
    $dir = Join-Path $root $case
    New-Item -ItemType Directory -Path $dir | Out-Null
    Copy-Item -LiteralPath (Join-Path $old 'LazBisPatcher.exe') -Destination $dir
    $app = Join-Path $dir 'LazBisPatcher.exe'
    $feed = Join-Path $dir 'fixture-feed'
    New-Item -ItemType Directory -Path $feed | Out-Null
    Copy-Item $new (Join-Path $feed '22-LazBisPatcher.exe')
    $oldHash = (Get-FileHash $app).Hash.ToLowerInvariant()
    $newHash = (Get-FileHash $new).Hash.ToLowerInvariant()
    $offer = @{ product = 1; tag = "v$TargetVersion"; version = $TargetVersion; releaseId = 2; assetId = 22;
        name = 'LazBisPatcher.exe'; bytes = (Get-Item $new).Length;
        sha256 = $newHash; notes = 'Generated Windows handoff test' }
    [IO.File]::WriteAllText((Join-Path $feed 'Patcher.json'), ($offer | ConvertTo-Json))
    if ($case -eq 'failed-launch') {
        New-Item -ItemType File (Join-Path $feed 'fail-startup') | Out-Null
    }
    $control = Join-Path $dir '.lazbis-self-update'
    $pending = Join-Path $control 'pending.json'
    $mq = Join-Path $dir 'MacroQuest'
    foreach ($relative in 'lua/lazbis/init.lua','config/lazbis.lua','resources/lazbis.db') {
        $sentinel = Join-Path $mq $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $sentinel) | Out-Null
        [IO.File]::WriteAllText($sentinel, 'Preserve disposable player bytes')
    }
    [IO.File]::WriteAllText((Join-Path $dir 'macroquest-folder.txt'), $mq)
    $before = @(Get-ChildItem -LiteralPath $mq -File -Recurse | Get-FileHash | ForEach-Object { $_.Path + ':' + $_.Hash })
    $started = $null
    $process = @()
    try {
        if ($case -eq 'download-interrupted') {
            $gate = Join-Path $feed 'pause-download'
            New-Item -ItemType File $gate | Out-Null
            $started = Start-Process -FilePath $app -ArgumentList '--fixture-update' -WindowStyle Hidden -PassThru
            $deadline = [DateTime]::UtcNow.AddSeconds(45)
            while (!(Test-Path (Join-Path $feed 'download-paused')) -and [DateTime]::UtcNow -lt $deadline) {
                if ($started.HasExited) { throw 'Source exited before staging gate' }
                Start-Sleep -Milliseconds 50
            }
            $candidate = Join-Path $control 'candidate.exe'
            if (!(Test-Path $candidate) -or (Test-Path $pending)) { throw 'Expected a candidate before the pending journal' }
            $partialBytes = (Get-Item $candidate).Length
            if ($partialBytes -le 0 -or $partialBytes -ge $offer.bytes) { throw 'Candidate is not a partial download' }
            $started.Kill()
            if (!$started.WaitForExit(10000)) { throw 'Actual staging process did not terminate' }
            $started.Dispose()
            if ((Get-FileHash $app).Hash.ToLowerInvariant() -ne $oldHash -or (Test-Path $pending)) { throw 'Termination changed installed EXE or prepared an update' }
            Remove-Item -LiteralPath $gate
            $started = Start-Process -FilePath $app -PassThru
            Invoke-UpdateButton $started
            if ($ExpectStagingBlock) {
                $deadline = [DateTime]::UtcNow.AddSeconds(10)
                do {
                    $started.Refresh()
                    $window = [Windows.Automation.AutomationElement]::FromHandle($started.MainWindowHandle)
                    $elements = $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
                    $text = ($elements | ForEach-Object { $_.Current.Name }) -join "`n"
                    if ($text -like '*earlier patcher update needs review*') { break }
                    Start-Sleep -Milliseconds 100
                } while ([DateTime]::UtcNow -lt $deadline)
                if ($text -notlike '*earlier patcher update needs review*' -or (Get-Item $candidate).Length -ne $partialBytes -or
                    (Get-FileHash $app).Hash.ToLowerInvariant() -ne $oldHash) { throw 'Expected legacy retry block was not reproduced' }
                Write-Host "REPRO actual process termination after $partialBytes staged bytes, no pending journal; original EXE intact; reopen works; retry blocked"
                continue
            }
            Write-Host "PASS actual process termination after $partialBytes staged bytes, no pending journal; reopened original EXE; retry started"
        } elseif ($case -eq 'interrupted') {
            # Create the exact persisted state immediately after replacement, before startup.
            New-Item -ItemType Directory -Path $control | Out-Null
            [IO.File]::WriteAllText((Join-Path $control 'owner'), 'LazBisPatcher preview self-update v1')
            Copy-Item $app (Join-Path $control 'previous.exe')
            Copy-Item $new $app -Force
            $record = @{ id = [Guid]::NewGuid().ToString('N'); nonce = ('a' * 64);
                oldHash = $oldHash; release = $offer; committed = $false }
            [IO.File]::WriteAllText($pending, ($record | ConvertTo-Json -Depth 4))
            # Recovery must replace corrupt owned helper bytes before executing them.
            if ([version]$TargetVersion -ge [version]'0.1.5') {
                [IO.File]::WriteAllText((Join-Path $control 'LazBisPatcher.Helper.exe'), 'corrupt cached helper')
                [IO.File]::WriteAllText((Join-Path $control 'helper-stage.exe'), 'interrupted extraction')
            }
            $started = Start-Process -FilePath $app -WindowStyle Hidden -PassThru
        } elseif ($case -eq 'success') {
            # Exercise the actual button and confirmation with only the bundled helper.
            $started = Start-Process -FilePath $app -PassThru
            Invoke-UpdateButton $started
        } else {
            $started = Start-Process -FilePath $app -ArgumentList '--fixture-update' -WindowStyle Hidden -PassThru
        }
        if (!$started.WaitForExit(90000) -or $started.ExitCode -ne 0) { throw "$case original process failed" }
        $expected = if ($case -in 'success','download-interrupted') { $newHash } else { $oldHash }
        $deadline = [DateTime]::UtcNow.AddSeconds(90)
        do {
            Start-Sleep -Milliseconds 200
            $process = @(Get-OwnedProcesses $dir | Where-Object { $_.Path -eq $app -and $_.MainWindowHandle -ne 0 })
            if (!(Test-Path $pending) -and (Get-FileHash $app).Hash.ToLowerInvariant() -eq $expected -and $process.Count -eq 1) { break }
        } while ([DateTime]::UtcNow -lt $deadline)
        if ((Test-Path $pending) -or (Get-FileHash $app).Hash.ToLowerInvariant() -ne $expected -or $process.Count -ne 1) {
            throw "$case did not settle into the expected executable and running window."
        }
        # A restored app can still be checking its feed and refuse the first close.
        $closeDeadline = [DateTime]::UtcNow.AddSeconds(45)
        do {
            $null = $process[0].CloseMainWindow()
            if ($process[0].WaitForExit(250)) { break }
        } while ([DateTime]::UtcNow -lt $closeDeadline)
        if (!$process[0].HasExited) { throw "$case window did not exit" }
        $after = @(Get-ChildItem -LiteralPath $mq -File -Recurse | Get-FileHash | ForEach-Object { $_.Path + ':' + $_.Hash })
        if (Compare-Object $before $after) { throw "$case changed player files" }
        if (Test-Path (Join-Path $control 'helper-stage.exe')) { throw 'Helper staging was not cleaned' }
        if (Test-Path (Join-Path $control 'download.json')) { throw 'Download ownership record was not cleaned' }
        $cached = Join-Path $control 'LazBisPatcher.Helper.exe'
        if ([version]$SourceVersion -ge [version]'0.1.5' -or $case -eq 'interrupted') {
            $helperVersion = if ($case -eq 'interrupted') { $TargetVersion } else { $SourceVersion }
            $expectedHelper = Join-Path $Output "bundled-helper-fixture-$helperVersion/LazBisPatcher.Helper.exe"
            if ((Get-FileHash $cached).Hash -ne (Get-FileHash $expectedHelper).Hash) { throw 'Extracted helper differs from bundled build' }
        }
        $entry = if ($case -eq 'success') { ', clicked Update Patcher and confirmed' } else { '' }
        Write-Host "PASS $case (fixture $SourceVersion -> fixture $TargetVersion), only LazBisPatcher.exe offered$entry, player files unchanged"
    } finally {
        # Only test-owned processes with exact executable paths.
        if (!$dir.StartsWith($fixtureRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture root changed' }
        Get-OwnedProcesses $dir | ForEach-Object { try { $_ | Stop-Process -ErrorAction SilentlyContinue } finally { $_.Dispose() } }
        if ($started) { $started.Dispose() }
        foreach ($owned in $process) { $owned.Dispose() }
    }
}
Write-Host "Windows handoff cases passed. Fixtures retained at $root"
