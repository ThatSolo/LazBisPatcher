# Maintaining LazBisPatcher

LazBisPatcher is a portable Windows x64 WPF app. [Player instructions](README.md) cover installation and use. [Verification](VERIFICATION.md) records the current candidate and test limits.

## Authorship and provenance

We originally built AdventureTimePatcher under drel-git, then transferred its GitHub repository to Sebbun. LazBisPatcher adapts that original work, including the WPF layout and update flow represented by checkpoint `ffdd08eda7f272c828b596a510875de710328874`. Its palette follows our TurboPatcher work, and the LazBis icon is a new monogram. LazBisPatcher follows the same handoff model with ThatSolo.

Preserve the attribution and terms for any specific third-party code or assets carried forward. The runtime licenses cover the bundled runtime components; they do not choose a license for the patcher source.

## Trusted update feeds

| Product | GitHub repository | Update source |
| --- | --- | --- |
| LazBis | ThatSolo/lazbis | `main` commit, with a literal `meta.version` in `init.lua` |
| LazBisPatcher | ThatSolo/LazBisPatcher | Latest stable GitHub Release with `LazBisPatcher.exe` |

For LazBis, the app resolves `main` once, reads `init.lua` at that exact commit as text, and compares its version with the last verified installed version. It supports `1.0` and three-part versions such as `1.0.1`. It pins the selected commit and reviewed file paths through download and verification. Each file's length and Git blob identity must match that commit's tree before the installer reviews or writes it. Installed records retain SHA-256 values for file ownership.

Only a newer version offers an update. A push with the same version does not replace a verified installation. Verify / repair uses the recorded commit and inventory, even if main moves. An older release-based installation keeps its original asset ID, length, SHA-256, version and ownership hashes for repair. A later version bump can upgrade it to a commit-based install. If the pinned source is unavailable, repair fails clearly rather than substituting current main. A failed main check does not prevent trying repair from the recorded source. New installs use the commit selected during their check.

The matching version entry in the existing `CHANGELOG` is optional update text. The app reads at most 64 KiB, allows five seconds for that optional read, and verifies the bytes against the checked commit's Git blob identity. Missing, unverified or unmatched notes use generic text and do not block a valid update. `CHANGELOG` stays outside the 13 managed install paths. ThatSolo does not need to edit it to release a version.

For an unrecorded copy, each managed file is assessed by bytes: keep a target match, add a missing file, and allow replacement of a reviewed official 1.0 SHA-256 match. Other differences are conflicts. The folder name and Lua-reported version do not grant file ownership. Recorded installations continue to use their saved hashes and pinned repair source. A verified install record is written only after successful apply.

Patcher self-updates still use GitHub Releases. Keep the compiled production repository as `ThatSolo/LazBisPatcher`. HTTPS, Git object identities and asset hashes bind downloads to the selected GitHub response; they do not independently authenticate its publisher. Control of the two publisher repositories controls these feeds.

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

The portable launch check starts with only the EXE and checks the main screen, version, absence of notices UI and normal close. The fixture harness clicks Update Patcher and confirms the successful update, then checks relaunch, failed-startup rollback, constructed interrupted-swap recovery, and an actual process termination during a gated partial download followed by reopen and retry. It also checks helper extraction/repair and unchanged disposable LazBis, config and database files. These local feed tests do not establish live GitHub update readiness.

The smoke suite needs the reviewed LazBis 1.0 ZIP. If it is not already available locally:

```powershell
gh release download 1.0 --repo ThatSolo/lazbis --pattern lazbis.zip --dir out\lazbis-1.0
```

Run the smoke suite against the release EXE:

```powershell
dotnet run --project tests/LazBisPatcher.Smoke -c Release -- out\lazbis-1.0\lazbis.zip out\release-0.1.5\LazBisPatcher.exe
.\scripts\Test-LazBisUi.ps1
```

The installer checks use the real pinned 1.0 ZIP for legacy compatibility and a generated LazBis 1.0.1 update. Source-feed and installer checks cover optional changelog fallbacks, direct manual 1.0 upgrades, missing files, local edits, cancellation, unknown forks, manual fixture moves and a clean install, plus a version bump, a push without a bump, main moving between check and download, pinned repair, legacy records, unavailable sources, bad blob identities, conflicts, rollback and player-file preservation. Executable checks inspect the candidate bytes and recovery records. Add `--live` as the final argument only when deliberately checking the real LazBis feed and installing into a disposable folder. Report network failures separately from local test results.

If Windows blocks the unsigned build script, use `pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Preview.ps1 -Version 0.1.5`. For the build command with two version values, use `pwsh -NoProfile -ExecutionPolicy Bypass -Command "& .\scripts\Build-Preview.ps1 -Fixture -Version '0.1.5','0.1.6'"`. This affects that PowerShell process only.

## Release LazBisPatcher

Upload exactly one `LazBisPatcher.exe` from the matching release build. It is both the player download and the self-update asset. Check its Windows x64 PE architecture, embedded version and SHA-256. Include release notes with a link to [runtime licenses and notices](docs/RUNTIME_NOTICES.md) at the documented source revision. Keep the exact build commit, asset hash and test results in the maintainer verification record. Player release notes can stay short.

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

For example, to update LazBis from 1.0 to 1.0.1:

1. Change the literal `meta.version` in `init.lua` from `1.0` to `1.0.1`.
2. Push or merge that change to `main` in `ThatSolo/lazbis` with the code intended for 1.0.1. The version bump and its code should land together so a player checking that commit receives the intended version.

That is the only release-specific work required in LazBis. No GitHub Release, tag, ZIP asset or workflow run is needed. `meta.version` is both the in-game version and the version the patcher compares. Do not reuse a version for different code that installed players should receive. Subsequent pushes without a bump will not update an existing verified copy; new installs can still select the current main commit.

The supported paths remain the 13-file inventory in [LazBis10.json](src/LazBisPatcher.Core/LazBis10.json). The patcher fetches only those files. Added, removed or renamed managed paths still require coordination, inventory review and a patcher build that supports them. Player configuration, databases and untracked files remain outside that list.

For maintainer verification, this validator resolves the live main commit, checks its version and files, and installs into a disposable MacroQuest-shaped folder:

```powershell
.\scripts\Test-Release.ps1 -Product LazBis
```

The smoke suite also tests a generated 1.0 to 1.0.1 update and older installation records. MacroQuest can stay open. Before applying changes or moving the old script folder, run `/lua stop lazbis` on every character using it. If that copy runs the helper, also run `/lua stop lazbis/bg` on those characters. Keep patcher EXE releases in the separate section above.

## Recovery and safety limits

Normal launch needs only the app EXE. Update or recovery extracts the verified bundled helper into `.lazbis-self-update`. Existing helper bytes are checked before use and replaced from the bundle when needed. Reopen the app after an interruption.

Patcher downloads write `download.json` under the updater lock before creating `candidate.exe`. After an interrupted download, startup or retry can discard only that owned candidate when the installed EXE still matches the record and no prepared journal, prior EXE, startup receipt or restore file exists. A written `pending.json` keeps the prepared update on the existing recovery path.

An older journal-less candidate without that ownership record is preserved. Close all patcher copies and back up the entire `.lazbis-self-update` folder. If `pending.json`, `previous.exe`, `ready.json` or `restore.exe` exists, keep all files for maintainer review. Otherwise move only the unrecorded `candidate.exe` outside the control folder, then reopen and retry. Do not remove individual journals or ownership records to bypass a recovery warning.

For an unverified fork switch, follow the [player instructions](README.md#switch-from-another-lazbis-copy). Back up the entire old script folder and its data before manually moving `lua\lazbis` outside `lua`. The patcher does not move, merge or migrate it. Moving scripts does not isolate `resources\lazbis.db` or `config\lazbis_lockouts.lua`; unknown compatibility calls for a separate MacroQuest test copy with its own data.

If the app cannot start and the cached helper exists, close all patcher copies and run this from the app folder:

```powershell
& .\.lazbis-self-update\LazBisPatcher.Helper.exe recover 0 0 "$PWD"
```

The last argument supplies the app folder. Keep recovery files when the helper requests review. Avoid deleting pending state or sweeping shared .NET extraction caches.

Use one cooperating writer on a local writable drive without directory links. MacroQuest can stay open. Run `/lua stop lazbis` on every character using it before applying changes or moving the old folder. If that copy runs the helper, also run `/lua stop lazbis/bg`. The patcher does not detect running scripts. LazBis writes are limited to the reviewed inventory beneath `lua/lazbis`. Its state, rollback data and latest committed backup are under `lua/.lazbis-patcher`. Legacy modules are not removed. Power-loss and hostile same-user filesystem safety are not claimed. The EXE is unsigned.

The [runtime notice review](docs/RUNTIME_NOTICES.md) describes the included texts and open redistribution questions. [Verification](VERIFICATION.md) keeps the unresolved legacy timeout and pending live feed check visible.
