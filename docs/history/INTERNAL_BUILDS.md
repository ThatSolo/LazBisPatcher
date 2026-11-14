# Earlier build evidence

These results describe earlier builds. Their asset names, packaging and test commands have been superseded. The [current verification record](../../VERIFICATION.md) identifies the present candidate and its limits.

| Build or version pair | Useful recorded result | Evidence status |
| --- | --- | --- |
| Two-file fixtures 0.1.3 to 0.1.4 | Update/relaunch, failed-startup rollback and interrupted-swap recovery passed, 3/3 | Earlier package layout; superseded by current portable fixtures |
| Two-file fixture 0.1.4 to earlier portable fixture 0.1.5 | All three update/recovery cases passed in the final recorded run | Earlier update asset name; not proof of the current feed |
| Earlier portable fixtures 0.1.5 to 0.1.6 | All three cases passed, including helper extraction/repair and player-file preservation | Earlier build with the old update asset name; superseded by the current one-file contract tests |
| Two-file release 0.1.4 to earlier portable release 0.1.5 | Original sidecar helper applied a prepared candidate and relaunched it; disposable player files stayed unchanged | Target EXE SHA-256 `ab43388cd31591f1fa9e84e18eda2ad99ce38413838dea7370d08d062d7acd55`; local helper probe, not a live-feed UI update |
| Earlier 0.1.5 build after removing notices UI | Fresh single-EXE main-screen launch and all five smoke groups passed | EXE SHA-256 `1c5da44c14c831fe45c89c002ea746c70a4c0e2d2a9afe8cabdf1ed906af9064`; superseded by the current candidate |

One earlier 0.1.4 to 0.1.5 fixture attempt timed out even though a later fresh run passed. The cause and fix remain unverified. The [known limits](../../VERIFICATION.md#known-limits-and-pending-checks) preserve the supported account of that failure.

Older notes also reported Windows 0.1.0 to 0.1.1 fixture checks and Linux-host smoke checks. Those reports were not rerun in the current verification pass and do not establish support for another platform or the current public feed.
