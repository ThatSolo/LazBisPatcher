param(
    [string]$Output = (Join-Path $PSScriptRoot '../out'),
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$SourceVersion = '0.1.5',
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$TargetVersion = '0.1.6',
    [ValidateSet('success','failed-launch','interrupted')]
    [string[]]$Cases = @('success','failed-launch','interrupted')
)
$ErrorActionPreference = 'Stop'
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
        if ($case -eq 'interrupted') {
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
        } else {
            $started = Start-Process -FilePath $app -ArgumentList '--fixture-update' -WindowStyle Hidden -PassThru
        }
        if (!$started.WaitForExit(90000) -or $started.ExitCode -ne 0) { throw "$case original process failed" }
        $expected = if ($case -eq 'success') { $newHash } else { $oldHash }
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
        $cached = Join-Path $control 'LazBisPatcher.Helper.exe'
        if ([version]$SourceVersion -ge [version]'0.1.5' -or $case -eq 'interrupted') {
            $helperVersion = if ($case -eq 'interrupted') { $TargetVersion } else { $SourceVersion }
            $expectedHelper = Join-Path $Output "bundled-helper-fixture-$helperVersion/LazBisPatcher.Helper.exe"
            if ((Get-FileHash $cached).Hash -ne (Get-FileHash $expectedHelper).Hash) { throw 'Extracted helper differs from bundled build' }
        }
        Write-Host "PASS $case (fixture $SourceVersion -> fixture $TargetVersion), only LazBisPatcher.exe offered, player files unchanged"
    } finally {
        # Only test-owned processes with exact executable paths.
        if (!$dir.StartsWith($fixtureRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture root changed' }
        Get-OwnedProcesses $dir | ForEach-Object { try { $_ | Stop-Process -ErrorAction SilentlyContinue } finally { $_.Dispose() } }
        if ($started) { $started.Dispose() }
        foreach ($owned in $process) { $owned.Dispose() }
    }
}
Write-Host "Windows handoff cases passed. Fixtures retained at $root"
