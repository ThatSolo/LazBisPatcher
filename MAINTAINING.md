# Maintaining LazBisPatcher

LazBisPatcher is a portable Windows x64 WPF app. [Player instructions](README.md) cover installation and use. [Verification](VERIFICATION.md) records the current candidate and test limits.

## Authorship and provenance

We originally built AdventureTimePatcher under drel-git, then transferred its GitHub repository to Sebbun. LazBisPatcher adapts that original work, including the WPF layout and update flow represented by checkpoint `ffdd08eda7f272c828b596a510875de710328874`. Its palette follows our TurboPatcher work, and the LazBis icon is a new monogram. LazBisPatcher follows the same handoff model with ThatSolo.

Preserve the attribution and terms for any specific third-party code or assets carried forward. The runtime licenses cover the bundled runtime components; they do not choose a license for the patcher source.

## Trusted release feeds

| Product | GitHub repository | Stable release asset |
| --- | --- | --- |
| LazBis | ThatSolo/lazbis | `lazbis.zip` |
| LazBisPatcher | ThatSolo/LazBisPatcher | `LazBisPatcher.exe` |

The app checks each repository's latest stable release over HTTPS. The patcher repository is compiled into the app; keep its production value as `ThatSolo/LazBisPatcher`. Access to those publisher accounts controls the trusted release feeds. Asset IDs, lengths and SHA-256 values bind downloads to the selected response. They do not independently authenticate the publisher.

## Build and test

Use Windows x64 and .NET SDK 10.0.301, or a compatible patch selected by `global.json`. Start from a clean checkout of the intended source commit. Record its full SHA before building and retain that value with the asset hash. A later documentation commit is not the EXE's build commit.

```powershell
git status --short
$sourceCommit = git rev-parse HEAD
.\scripts\Build-Preview.ps1 -Version 0.1.5
.\scripts\Test-PortableLaunch.ps1 -Executable out\release-0.1.5\LazBisPatcher.exe -Version 0.1.5
.\scripts\Build-Preview.ps1 -Fixture -Version 0.1.5,0.1.6
.\scripts\Test-WindowsHandoff.ps1 -SourceVersion 0.1.5 -TargetVersion 0.1.6
```

The build publishes the helper first, records its SHA-256 and bundles it inside the app. The release output is one `LazBisPatcher.exe`. Fixture builds use a local feed and test flags. Keep those builds off GitHub Releases.

The portable launch check starts with only the EXE and checks the main screen, version, absence of notices UI and normal close. The fixture harness checks update and relaunch, failed-startup rollback, interrupted-swap recovery, helper extraction and repair, and unchanged disposable LazBis, config and database files. These local feed tests do not establish live GitHub update readiness.

The smoke suite needs the reviewed LazBis 1.0 ZIP. If it is not already available locally:

```powershell
gh release download 1.0 --repo ThatSolo/lazbis --pattern lazbis.zip --dir out\lazbis-1.0
```

Run the smoke suite against the release EXE:

```powershell
dotnet run --project tests/LazBisPatcher.Smoke -c Release -- out\lazbis-1.0\lazbis.zip out\release-0.1.5\LazBisPatcher.exe
```

The installer checks use the real pinned 1.0 ZIP and a generated LazBis 1.0.1 update. They cover adoption, repair, reinstall, hash rejection, rollback and user-file preservation. Executable checks inspect the candidate bytes and recovery records. Add `--live` as the final argument only when deliberately checking the real LazBis feed and installing into a disposable folder. Report network failures separately from local test results.

If Windows blocks the unsigned build script, use `pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Preview.ps1 -Version 0.1.5`. For the build command with two version values, use `pwsh -NoProfile -ExecutionPolicy Bypass -Command "& .\scripts\Build-Preview.ps1 -Fixture -Version '0.1.5','0.1.6'"`. This affects that PowerShell process only.

## Release LazBisPatcher

Upload exactly one `LazBisPatcher.exe` from the matching release build. It is both the player download and the self-update asset. Check its Windows x64 PE architecture, embedded version and SHA-256. Include release notes with a link to [runtime licenses and notices](docs/RUNTIME_NOTICES.md) at the documented source revision. The notes must identify the source used to build the EXE and any later documentation revision separately.

For a new version with no existing release, set the matching version, retain `$sourceCommit` from the clean source checkout used for the build, and prepare the release notes file before running:

```powershell
$version = '0.1.5'
$asset = "out\release-$version\LazBisPatcher.exe"
$notesFile = 'release-notes.md'
Get-FileHash -LiteralPath $asset -Algorithm SHA256
gh release create "v$version" $asset --repo ThatSolo/LazBisPatcher --draft --target $sourceCommit --notes-file $notesFile
```

The example creates a draft. Before publishing, compare the uploaded digest and byte count with the tested EXE, confirm exactly one asset, and verify that the stable tag resolves to the intended source commit. An existing tag is not moved by changing the release target field. Existing releases need a deliberate review rather than rerunning the create command.

After publication, check the live feed:

```powershell
.\scripts\Test-Release.ps1 -Product Patcher
```

Compare the README's direct download with the verified EXE. For later public versions, also update a disposable copy of the prior public release through the app. Confirm the new version, no pending recovery, and unchanged LazBis, config and database hashes. Older internal builds can switch manually by downloading the new EXE.

## Releasing a LazBis update

LazBis releases are separate from LazBisPatcher EXE releases. For example, to update LazBis from 1.0 to 1.0.1:

1. Set `meta.version` in `init.lua` to `1.0.1` for the in-game version. The patcher detects updates from the published GitHub tag `v1.0.1` in `ThatSolo/lazbis`, not from `meta.version`. Changing that Lua value alone does not publish an update. Keep the existing `1.0` release and its bytes unchanged.
2. Prepare exactly one asset named `lazbis.zip` with the layout below. Keep the reviewed 13 paths in [LazBis10.json](src/LazBisPatcher.Core/LazBis10.json). Contents can change, but added, removed or renamed paths require inventory review and an updated patcher that supports them. Exclude player configuration, databases and local state.
3. Write release notes for `v1.0.1`, upload the complete ZIP and wait for the upload to finish. Publish it as the latest stable release, not a draft or prerelease.
4. Run the release validator, then test a fresh install and an update from a recorded 1.0 installation in disposable MacroQuest-shaped folders containing `lua` and `config`. Check that the patcher offers 1.0.1 and records it after installation. Verify player configuration, database and local file paths and hashes stay unchanged. Test repair if files need restoring. Stop LazBis and its background helper before applying changes.

The ZIP must have one top-level `lazbis/` folder, with no extra wrapper or root files:

```text
lazbis/
  init.lua
  bg.lua
  bis.lua
  spells.lua
  bis.png
  icon_lazbis.png
  LICENSE
  README.md
  spoiler.txt
  images/
    dsk.png
    need.png
    sebilis.png
    veksar.png
```

Validate the published LazBis release:

```powershell
.\scripts\Test-Release.ps1 -Product LazBis
```

The smoke suite's generated 1.0.1 fixture is useful local coverage, but does not replace validation and a disposable install of the actual published release.

## Recovery and safety limits

Normal launch needs only the app EXE. Update or recovery extracts the verified bundled helper into `.lazbis-self-update`. Existing helper bytes are checked before use and replaced from the bundle when needed. Reopen the app after an interruption.

If the app cannot start and the cached helper exists, close all patcher copies and run this from the app folder:

```powershell
& .\.lazbis-self-update\LazBisPatcher.Helper.exe recover 0 0 "$PWD"
```

The last argument supplies the app folder. Keep recovery files when the helper requests review. Avoid deleting pending state or sweeping shared .NET extraction caches.

Use one cooperating writer on a local writable drive without directory links. Stop LazBis and its background helper on every character before applying changes; the patcher does not detect running scripts. LazBis writes are limited to the reviewed inventory beneath `lua/lazbis`. Its state, rollback data and latest committed backup are under `lua/.lazbis-patcher`. Legacy modules are not removed. Power-loss and hostile same-user filesystem safety are not claimed. The EXE is unsigned.

The [runtime notice review](docs/RUNTIME_NOTICES.md) describes the included texts and open redistribution questions. [Verification](VERIFICATION.md) keeps the unresolved legacy timeout and pending live feed check visible.
