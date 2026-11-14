param(
    [Parameter(Mandatory)][ValidateSet('LazBis','Patcher')][string]$Product,
    [string]$PatcherRepository = 'ThatSolo/LazBisPatcher'
)
$ErrorActionPreference = 'Stop'
$repo = if ($Product -eq 'LazBis') { 'ThatSolo/lazbis' } else { $PatcherRepository }
if ($repo -notmatch '^[A-Za-z0-9_-]{1,39}/[A-Za-z0-9_.-]{1,100}$' -or ($repo -split '/')[1] -in '.','..') {
    throw 'Expected a GitHub owner/repository.'
}
$name = if ($Product -eq 'LazBis') { 'lazbis.zip' } else { 'LazBisPatcher.exe' }
$headers = @{ 'User-Agent' = 'LazBisPatcher-release-check'; 'X-GitHub-Api-Version' = '2022-11-28';
    Accept = 'application/vnd.github+json' }
$release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers $headers -TimeoutSec 30
if ($release.draft -or $release.prerelease) { throw 'Publish a stable release.' }
if ($Product -eq 'Patcher' -and @($release.assets).Count -ne 1) { throw 'Publish exactly one LazBisPatcher.exe asset.' }
$tag = $release.tag_name
if ($tag -notmatch '^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -and
    !($Product -eq 'LazBis' -and $tag -eq '1.0')) { throw "Invalid release tag '$tag'. Use vMAJOR.MINOR.PATCH." }
$assets = @($release.assets | Where-Object { $_.name -ceq $name })
if ($assets.Count -ne 1 -or $assets[0].state -ne 'uploaded' -or $assets[0].size -le 0 -or
    $assets[0].digest -notmatch '^sha256:[a-f0-9]{64}$') { throw "Publish one fully uploaded $name asset with a SHA-256 digest." }
$asset = $assets[0]
$dir = Join-Path ([IO.Path]::GetTempPath()) ('LazBisReleaseCheck-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $file = Join-Path $dir $name
    $headers.Accept = 'application/octet-stream'
    Invoke-WebRequest "https://api.github.com/repos/$repo/releases/assets/$($asset.id)" -Headers $headers -OutFile $file -TimeoutSec 180
    if ((Get-Item $file).Length -ne $asset.size -or
        (Get-FileHash $file -Algorithm SHA256).Hash -ne $asset.digest.Substring(7)) { throw 'Downloaded bytes differ from the published asset.' }
    if ($Product -eq 'Patcher') {
        $info = (Get-Item $file).VersionInfo
        $version = "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)"
        if ($version -ne $tag.TrimStart('v')) { throw "Tag $tag and executable version $version differ. Rebuild and publish a new tag." }
    } else {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $manifest = Get-Content (Join-Path $PSScriptRoot '../src/LazBisPatcher.Core/LazBis10.json') -Raw | ConvertFrom-Json
        $expected = @($manifest | ForEach-Object { 'lazbis/' + $_.path })
        $archive = [IO.Compression.ZipFile]::OpenRead($file)
        try {
            $entries = @($archive.Entries | Where-Object { $_.FullName -cnotin 'lazbis/','lazbis/images/' } | ForEach-Object { $_.FullName })
            if ($entries.Count -ne $expected.Count -or (Compare-Object $entries $expected -CaseSensitive)) {
                throw 'ZIP file list differs from the reviewed preview inventory. Review its contents before publishing.'
            }
        } finally { $archive.Dispose() }
    }
    Write-Host "PASS $repo $tag asset $($asset.id) $name $($asset.digest)"
} finally {
    if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file }
    Remove-Item -LiteralPath $dir
}
