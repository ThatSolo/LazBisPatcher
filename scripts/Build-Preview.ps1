param(
    [string]$Output = (Join-Path $PSScriptRoot '../out'),
    [switch]$Fixture,
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string[]]$Version = @('0.1.5')
)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
$project = Join-Path $PSScriptRoot '../src'
$variant = if ($Fixture) { 'fixture' } else { 'release' }
foreach ($buildVersion in $Version) {
    $dest = Join-Path $Output "$variant-$buildVersion"
    $common = @('-c','Release','-r','win-x64','--self-contained','true',
        '-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true','-p:DebugType=none',
        "-p:Version=$buildVersion","-p:FixtureFeed=$($Fixture.IsPresent.ToString().ToLowerInvariant())")
    $helperOutput = Join-Path $Output "bundled-helper-$variant-$buildVersion"
    & dotnet publish (Join-Path $project 'LazBisPatcher.Helper') @common -o $helperOutput
    if ($LASTEXITCODE -ne 0) { throw "Helper publish failed for $buildVersion" }
    $helper = Join-Path $helperOutput 'LazBisPatcher.Helper.exe'
    $hash = (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash.ToLowerInvariant()
    & dotnet publish (Join-Path $project 'LazBisPatcher.Gui') @common -o $dest "-p:BundledHelperPath=$helper" "-p:BundledHelperSha256=$hash"
    if ($LASTEXITCODE -ne 0) { throw "GUI publish failed for $buildVersion" }
}
