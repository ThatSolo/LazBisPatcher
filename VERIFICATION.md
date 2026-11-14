# Verification

Candidate: LazBisPatcher 0.1.5 for Windows x64. The clean release repository contains the initial reviewed tree and reviewed changes for commit-pinned LazBis updates, owned download retry and manual-install recognition. It does not contain the private development archive history.

## Candidate identity and source provenance

Build from the clean reviewed source commit and record its full SHA before building. The asset is `LazBisPatcher.exe`, an AMD64 PE with FileVersion `0.1.5.0` and ProductVersion `0.1.5+<full build commit SHA>`.

The local maintainer verification record captures the exact build commit, SHA-256, byte count and results for the EXE attached to the private draft. This procedure is committed before building. Player release notes stay short and link to the required notices at that source revision. Earlier candidate hashes and source revisions do not identify this build. Do not treat a later documentation revision as an EXE build commit.

## Checks required for the new candidate

Record actual results in the maintainer verification record. Until recorded there, these checks are pending:

| Check | Required evidence |
| --- | --- |
| Source identity | Clean working tree, recorded full SHA |
| Packaged EXE | AMD64, embedded version and source SHA, SHA-256 and byte count |
| Exact EXE on native Windows | New writable folder containing only the EXE; main screen and v0.1.5 footer; no notices popup or link; normal close; no separate helper download |
| Single-EXE fixtures, 0.1.5 to 0.1.6 | Local feed offering only `LazBisPatcher.exe`; successful relaunch through the Update Patcher button, failed-startup rollback, constructed interrupted-swap recovery, and actual process termination during staging followed by reopen/retry |
| Player files and helper | Unchanged disposable LazBis, configuration and database file paths and hashes; bundled helper hash verified; corrupt helper replaced; no pending journal or helper staging left after recovery |
| LazBis source feed | 1.0 to 1.0.1; no-bump push; main moves before download; pinned repair; old release record compatibility; unavailable source and unreadable version fail clearly; unchanged player files |
| Manual installs and forks | Pristine unrecorded official 1.0 upgrades directly to 1.0.1; missing file, local edit and cancellation cases; unknown fork reporting 1.0 stays unchanged; manual move then clean install; player data hashes preserved |
| Native LazBis UI | Official upgrade preview and cancellation; Existing LazBis copy found blocks Apply; exact stop commands, fork-switch/shared-data guidance; manual move and clean install |
| Optional notes | Matching CHANGELOG entry at checked commit; moved main; missing file/entry, unavailable or unverified notes use generic text without changing the managed list |
| Smoke suite | Pinned LazBis 1.0 ZIP and generated 1.0.1 installer checks; candidate PE and swap/recovery byte checks |
| Distribution | Four unchanged notice resources, matching repository texts, source-pinned notice link in draft notes, exactly one uploaded EXE with matching GitHub digest |

Fixture builds use test flags and a local feed. Their EXEs differ from the production candidate and must stay off GitHub Releases. The exact production EXE's fresh launch and smoke byte checks are separate from end-to-end native self-update fixtures. [MAINTAINING.md](MAINTAINING.md) gives the commands.

## LazBis source verification

The source fixtures use a controlled HTTP transport with the production GitHub feed and installer. They check that downloads use the selected commit after main moves, that equal versions keep the recorded repair source, and that local edits remain conflicts. Legacy JSON records omit the new source field and retain their version, release asset pin and ownership hashes. An optional local earlier-build installation can be supplied to the smoke suite through `LAZBIS_LEGACY_FIXTURE_ROOT`; the test repairs and upgrades a disposable copy without modifying that source folder.

`scripts/Test-Release.ps1 -Product LazBis` checks the real main feed and installs only into a disposable folder. It reads Lua as text and never executes it. Network or host socket failures must be reported separately from fixture results.

## Reproduced failures and evidence boundaries

Starting from reviewed `b83b5b4a3794dcd2b4a9ac64b56f80268b0cc8f3`, a test-only feed gate paused the unchanged updater after 65,536 candidate bytes. The native Windows process was actually terminated before `pending.json` existed. The installed EXE stayed intact and reopened, but the next update was blocked by the journal-less candidate. The gate changes test transport timing only; it is compiled out of production builds.

The earlier packaged core also produced two conflicts when a pristine manual official 1.0 installation previewed a generated 1.0.1 update with no patcher record. Neither preview changed the files or wrote a verified install record.

The final native partial-download fixture must repeat the actual kill, reopen the original EXE and complete retry. Unit cases that construct download ownership, prepared journals or rollback evidence are separate safety checks. The interrupted-swap fixture constructs the persisted post-swap state; it is not evidence of killing the process during a real swap.

The native LazBis UI fixture uses test builds and a local feed. The production candidate's fresh launch and tests against its extracted core are identified separately in the local maintainer record. These checks do not claim an end-to-end live install through the packaged GUI.

## Earlier evidence

Earlier builds passed native launch, fixture and smoke checks, but they do not prove this clean build. [Earlier build evidence](docs/history/INTERNAL_BUILDS.md) identifies useful historical results and superseded version pairs.

## Known limits and pending checks

- **Unresolved earlier timeout:** an earlier 0.1.4 to 0.1.5 fixture update did not settle before its deadline. The old executable and prepared candidate/journal remained. Manually invoking that folder's original helper completed the update. A later fresh 0.1.4 to 0.1.5 fixture run passed all three cases. The cause of the earlier failure is unknown, and a fix has not been established. It had a prepared candidate/journal, unlike the separate pre-journal partial-download failure reproduced above. This result used the earlier package and asset contract. If a timeout reproduces on the supported single-EXE path, investigate and resolve it before preparing the final draft.
- **Final patcher GitHub feed check: pending.** After the stable release is available at `ThatSolo/LazBisPatcher`, run `scripts/Test-Release.ps1 -Product Patcher` and compare the direct player download with the verified EXE. Local fixtures do not establish readiness of that final endpoint. For later public versions, also verify an actual in-app upgrade from the prior public release and unchanged LazBis/config/database hashes. Older internal builds can switch manually to the new EXE.
- The EXE is unsigned. HTTPS and asset hashes depend on trust in the publisher account and selected GitHub response; they are not independent publisher signatures.
- Power-loss and hostile same-user filesystem safety have not been established. Use a local writable drive, avoid directory links, and stop LazBis and its background helper before applying changes.
- [Runtime redistribution questions](docs/RUNTIME_NOTICES.md#redistribution-questions) remain open. Confirm source licensing, any specific third-party code or asset terms, and branding before public distribution. This record does not establish full compliance or serve as a complete security audit.
