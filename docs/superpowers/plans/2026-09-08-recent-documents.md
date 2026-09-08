# Batch recent-document cleanup

Goal: clear AutoCAD's recent-file entries once after a batch, without competing with its workers or failing a completed merge.

The agreed design places cleanup in the PowerShell coordinator. A per-user named mutex coordinates worker launches and cleanup across batch coordinators. Cleanup skips while any acad/accoreconsole process exists (conservative across versions and users). Registry discovery is limited to the selected executable's release and matching AcadLocation; only File<number> values in Recent File List are removed. Unknown formats and other settings remain untouched. Errors are warnings and a separate JSON report. Actual DWGs are never deleted.

An independently launched AutoCAD does not acquire our mutex: the process check cannot provide atomic exclusion against manual launches. Host testing is necessary for each supported release and Start-tab implementation. This change applies to Start-MergeDwgBatch.ps1, not interactive MERGEDWG.

- [x] Add the registry cleanup helper and launch/cleanup gate.
- [x] Integrate once after worker completion; make run folders unique and confirm timed-out processes exit before freeing slots.
- [x] Test concurrent gates, busy hosts, absent/unknown registry data, failure handling, and repeated cleanup using fixtures rather than the real user profile.
- [x] Document behavior and verify PowerShell syntax and existing host-selection tests.

Validation: cleanup and worker-lifecycle tests passed in PowerShell 7.6.4 and Windows PowerShell 5.1, including a separate-process mutex check and 1000 fixture cleanup cycles. Existing host-selection tests and syntax/diff checks passed. No AutoCAD UI or real-profile cleanup was executed; release-specific Start-tab behavior remains unverified.
