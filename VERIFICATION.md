# Verification

Candidate: LazBisPatcher 0.1.5 for Windows x64. The clean release repository contains the initial reviewed tree and the change to commit-pinned LazBis updates. It does not contain the private development archive history.

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
| Single-EXE fixtures, 0.1.5 to 0.1.6 | Local feed offering only `LazBisPatcher.exe`; successful relaunch through the Update Patcher button, failed-startup rollback and interrupted-swap recovery |
| Player files and helper | Unchanged disposable LazBis, configuration and database file paths and hashes; bundled helper hash verified; corrupt helper replaced; no pending journal or helper staging left after recovery |
| LazBis source feed | 1.0 to 1.0.1; no-bump push; main moves before download; pinned repair; old release record compatibility; unavailable source and unreadable version fail clearly; unchanged player files |
| Smoke suite | Pinned LazBis 1.0 ZIP and generated 1.0.1 installer checks; candidate PE and swap/recovery byte checks |
| Distribution | Four unchanged notice resources, matching repository texts, source-pinned notice link in draft notes, exactly one uploaded EXE with matching GitHub digest |

Fixture builds use test flags and a local feed. Their EXEs differ from the production candidate and must stay off GitHub Releases. The exact production EXE's fresh launch and smoke byte checks are separate from end-to-end native self-update fixtures. [MAINTAINING.md](MAINTAINING.md) gives the commands.

## LazBis source verification

The source fixtures use a controlled HTTP transport with the production GitHub feed and installer. They check that downloads use the selected commit after main moves, that equal versions keep the recorded repair source, and that local edits remain conflicts. Legacy JSON records omit the new source field and retain their version, release asset pin and ownership hashes. An optional local earlier-build installation can be supplied to the smoke suite through `LAZBIS_LEGACY_FIXTURE_ROOT`; the test repairs and upgrades a disposable copy without modifying that source folder.

`scripts/Test-Release.ps1 -Product LazBis` checks the real main feed and installs only into a disposable folder. It reads Lua as text and never executes it. Network or host socket failures must be reported separately from fixture results.

## Earlier evidence

Earlier builds passed native launch, fixture and smoke checks, but they do not prove this clean build. [Earlier build evidence](docs/history/INTERNAL_BUILDS.md) identifies useful historical results and superseded version pairs.

## Known limits and pending checks

- **Unresolved earlier timeout:** an earlier 0.1.4 to 0.1.5 fixture update did not settle before its deadline. The old executable and prepared candidate/journal remained. Manually invoking that folder's original helper completed the update. A later fresh 0.1.4 to 0.1.5 fixture run passed all three cases. The cause of the earlier failure is unknown, and a fix has not been established. This result used the earlier package and asset contract. If a timeout reproduces on the supported single-EXE path, investigate and resolve it before preparing the final draft.
- **Final patcher GitHub feed check: pending.** After the stable release is available at `ThatSolo/LazBisPatcher`, run `scripts/Test-Release.ps1 -Product Patcher` and compare the direct player download with the verified EXE. Local fixtures do not establish readiness of that final endpoint. For later public versions, also verify an actual in-app upgrade from the prior public release and unchanged LazBis/config/database hashes. Older internal builds can switch manually to the new EXE.
- The EXE is unsigned. HTTPS and asset hashes depend on trust in the publisher account and selected GitHub response; they are not independent publisher signatures.
- Power-loss and hostile same-user filesystem safety have not been established. Use a local writable drive, avoid directory links, and stop LazBis and its background helper before applying changes.
- [Runtime redistribution questions](docs/RUNTIME_NOTICES.md#redistribution-questions) remain open. Confirm source licensing, any specific third-party code or asset terms, and branding before public distribution. This record does not establish full compliance or serve as a complete security audit.
