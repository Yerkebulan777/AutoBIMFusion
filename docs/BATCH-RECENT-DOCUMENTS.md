# Recent documents after batch merging

`tools/Start-MergeDwgBatch.ps1` automatically clears recognized `File<number>` entries in the selected AutoCAD installation's `Recent File List` after its workers finish. This includes earlier recent-file entries, not just the batch inputs. Pinned/unknown metadata and DWG files are not removed. The number-of-recent-files preference is unchanged. Interactive `MERGEDWG` does not trigger this cleanup.

All batch coordinators for the current Windows user share a named mutex around worker creation and cleanup. File processing remains parallel. Each run uses a unique temporary directory. A timed-out worker must actually exit before its parallel slot can be reused; failure to stop it aborts further scheduling.

Cleanup skips if any `acad` or `accoreconsole` process is still present, including another batch or an interactive window. It fails closed if process enumeration or registry access fails. The batch does not wait for unrelated windows or retry indefinitely. Cleanup errors do not change merge success. See the console and `recent-documents.json` in the printed batch workspace for status and removed-entry count.

Discovery uses the executable's product release and registry `AcadLocation`; unrecognized layouts are skipped. The implementation does not delete entire registry keys, Windows jump lists, or Recent folders. Start-tab behavior and pinned entries may vary by AutoCAD release and require host validation.

A manually launched AutoCAD does not participate in the mutex. Process checks before cleanup and each deletion reduce that race but cannot eliminate it. Keep interactive AutoCAD closed during the final cleanup if an empty history is required. There is no guarantee of complete history removal while unrelated applications are starting AutoCAD.

Verification: run `tools/Test-MergeDwgRecentDocuments.ps1` in a fresh PowerShell process (5.1 or later). It checks a real cross-process mutex and uses mock registry/process data for cleanup cases, including 1000 repetitions. To validate UI behavior, use a disposable Windows user/profile on each supported AutoCAD release, run a batch, inspect the JSON report, then reopen AutoCAD and inspect Recent Documents. Do not use the real working profile for this test.
