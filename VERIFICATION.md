# Verification

Candidate: LazBisPatcher 0.1.5 for Windows x64. The application source, update helper and notice resources start from the reviewed release tree. The clean release repository starts with one root commit, without the private development history.

## Candidate identity and source provenance

Build from the clean initial commit and record its full SHA before building. The asset is `LazBisPatcher.exe`, an AMD64 PE with FileVersion `0.1.5.0` and ProductVersion `0.1.5+<full build commit SHA>`.

The draft release notes record the exact build commit, SHA-256, byte count and results for that EXE. This procedure is committed before the build so the repository can retain one initial release commit. Earlier candidate hashes and source revisions are not the identity of this new build. Do not treat a later documentation revision as an EXE build commit.

## Checks required for the new candidate

Record actual results in the draft notes. Until recorded there, these checks are pending:

| Check | Required evidence |
| --- | --- |
| Source identity | Clean working tree, one root commit, recorded full SHA |
| Packaged EXE | AMD64, embedded version and source SHA, SHA-256 and byte count |
| Exact EXE on native Windows | New writable folder containing only the EXE; main screen and v0.1.5 footer; no notices popup or link; normal close; no separate helper download |
| Single-EXE fixtures, 0.1.5 to 0.1.6 | Local feed offering only `LazBisPatcher.exe`; successful relaunch, failed-startup rollback and interrupted-swap recovery |
| Player files and helper | Unchanged disposable LazBis, configuration and database file paths and hashes; bundled helper hash verified; corrupt helper replaced; no pending journal or helper staging left after recovery |
| Smoke suite | Pinned LazBis 1.0 ZIP and generated 1.0.1 installer checks; candidate PE and swap/recovery byte checks |
| Distribution | Four unchanged notice resources, matching repository texts, source-pinned notice link in draft notes, exactly one uploaded EXE with matching GitHub digest |

Fixture builds use test flags and a local feed. Their EXEs differ from the production candidate and must stay off GitHub Releases. The exact production EXE's fresh launch and smoke byte checks are separate from end-to-end native self-update fixtures. [MAINTAINING.md](MAINTAINING.md) gives the commands.

## Earlier evidence

Earlier builds passed native launch, fixture and smoke checks, but they do not prove this clean build. [Earlier build evidence](docs/history/INTERNAL_BUILDS.md) identifies useful historical results and superseded version pairs.

## Known limits and pending checks

- **Unresolved earlier timeout:** an earlier 0.1.4 to 0.1.5 fixture update did not settle before its deadline. The old executable and prepared candidate/journal remained. Manually invoking that folder's original helper completed the update. A later fresh 0.1.4 to 0.1.5 fixture run passed all three cases. The cause of the earlier failure is unknown, and a fix has not been established. This result used the earlier package and asset contract. If a timeout reproduces on the supported single-EXE path, investigate and resolve it before preparing the final draft.
- **Live GitHub feed check: pending.** After the stable release is available at `ThatSolo/LazBisPatcher`, run `scripts/Test-Release.ps1 -Product Patcher` and compare the direct player download with the verified EXE. Local fixtures do not establish readiness of that final endpoint. For later public versions, also verify an actual in-app upgrade from the prior public release and unchanged LazBis/config/database hashes. Older internal builds can switch manually to the new EXE.
- The EXE is unsigned. HTTPS and asset hashes depend on trust in the publisher account and selected GitHub response; they are not independent publisher signatures.
- Power-loss and hostile same-user filesystem safety have not been established. Use a local writable drive, avoid directory links, and stop LazBis and its background helper before applying changes.
- [Runtime redistribution questions](docs/RUNTIME_NOTICES.md#redistribution-questions) remain open. Confirm source licensing, any specific third-party code or asset terms, and branding before public distribution. This record does not establish full compliance or serve as a complete security audit.
