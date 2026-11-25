# Audit fixes: live test results

Civil 3D 2027 (net10 build of the audit branch), metric template, one Civil 3D session. 25 Nov 2026.
Scripts: `tests/run_save.bat`, `run_audit.bat` / `run_audit_rest.bat`, `run_transport_{edit,loop,down}.bat`,
`run_imports_{sewer,water,water_b3}.bat`. Raw output in `tests/results/`.

## Fixes made during the test

| # | Problem found | Fix |
|---|---|---|
| 33 | `diagnose_save` crashed Civil 3D (probe *file* filer), even in an empty drawing | Copy filer only; `filer:"file"` refused |
| 34 | `section_volume_tables`: Civil 3D 2027 leaves every new table open for write, so the drawing cannot be saved (S1) and quitting can crash | `AfterEnd` hook + `ReleaseStrayOpens` (open for notify, DowngradeOpen, Close); warns if a table stays open |

Fixes 35-42 (decided after the first run) are in `docs/AUDIT_FIXES.log`; verified with `run_transport_edit.bat`,
`run_fixes.bat` (ALL PASS, drawing still savable) and `run_imports_sewer.bat` on the rebuilt plugin.

## A. Transport

| # | Result | Notes |
|---|---|---|
| A1 | PASS | Dialog left open 131 s; client kept waiting, 13 progress notifications (≤10 s apart), edit applied |
| A2 | PASS after fix 35 | First run: no answer while the dialog was open (reads wait for the main thread). Now: answered in 2.0 s with `busy: true` |
| A3 | PASS after fix 36 | First run: refused only after the 30 s queue timeout. Now: refused at once with "Another MCP edit is still running"; resend applied; exactly 2 lines |
| A4 | PASS | Dry run, then the same request_id for real: applied |
| A5 | PASS | "Civil 3D is not reachable…" (no TaskGroup text) |
| A6 | PASS | 1095 reads during 18 MCPSTATUS runs, 0 errors |
| A7 | PASS | 512 alignments: totals correct, 510 default names flagged. (An edit takes at most 500 operations.) |

## B. Imports

| # | Result | Notes |
|---|---|---|
| B1 | PASS | WaterGEMS build, then the same request_id: recorded result, no KeyError, no second network |
| B2 | PASS after fix 37 | First run: the retry landed in the profile-layout phase and returned `built: false` without `pending`. Now: `pending: true` with a call-again note |
| B3 | NOT REPRODUCIBLE | Civil 3D 2027 allows the same pressure pipe name in different networks (P-20 exists in three networks), so nothing is renamed and no warning is due |

## C. Edit operations changed by the patch

| # | Result | Notes |
|---|---|---|
| C1 | PASS | Copied block reference keeps TAG1/TAG2; copied 3D polyline same length, moved 50 m |
| C2 | PASS | Copy stays on the layout |
| C3 | PASS | Checked in a DXF copy: nested reference keeps 2 attributes; new reference on the originals' layout. Heavy 2D polyline not testable live (no operation creates one; `surface_waterdrop polyline2d` gives LWPOLYLINE) |
| C4 | PASS | "give 'names' or 'handles'…"; pipes unchanged |
| C5 | PASS | "… is not a part of network …" |
| C6 | PASS | index "2", missing index, 1.5: all refused with clear messages |
| C7 | PASS | "'index' must be a whole number." |
| C8 | PASS | points_export / points_import / block_import / inspect_file: "must be a full path" |
| C9 | PASS | XR.BASE copy, block BBAL2 with two MATCH_LINE_EAST tags |
| C10 | NOT TRIGGERED | 119 parts × 14 views, run twice: 0 warnings, so the 200 cap was never reached (code reviewed only) |
| C11 | PASS | interval 0.001: 2001 samples per pipe, instant |
| C12 | PASS | "… use a larger interval." |
| C13 | PASS | "stopped at 5000 sample lines (station 149.97) …" |

## D. Behaviour records (decide the next fix)

| # | Observed |
|---|---|
| D1 | OK: `inner_diameter: 300` → "300 mm PVC Pipe" (mm, as the catalogue lists) |
| D2 | OK: pipes, fittings, labels and the pipe-run alignment/profile are erased; nothing orphaned |
| D3 | OK: after rollback the current text/dim/mleader style and annotation scale are unchanged and valid |
| D4 | Fixed (38): replace redefines the block in place; the drawing's layers/styles never change (verified: 10 m → 20 m, no layer changed) |
| D5 | Fixed (39): chained on each side (verified: shoulder attached at ETW, EPS 4.5 m beyond ETW 3.6 m) |
| D6 | Fixed (40): connected pipes skipped, counted in `connectedPairsSkipped` (verified: found 0, skipped 1) |
| D7 | Fixed (41): temp file, replace on success only (verified both ways) |
| D8 | Fixed (42): already-labelled parts skipped; `network_styles` restyles existing labels (verified: counts unchanged on re-run). `section_views_create` / `section_volume_tables` refuse duplicates (Civil 3D) |

## E. Save failure

| # | Result |
|---|---|
| E6 | PASS: empty metric drawing, `diagnose_save`: savable, 6098 objects (after fix 33) |
| E1 | FOUND, then CLEAN: before fix 34 the first `section_volume_tables` edit left the drawing unsavable; after it, all five suites (70 edits) ALL PASS and `SAVE CHECK CLEAN` |
| E2 | Cause: section view volume tables open for write (fix 34) |
| E3 | PASS: QUIT → No closes cleanly, no message, no crash (after run_save, and again after this test with 28 volume tables) |
| E4 | Reproduced in fresh drawings with the volume-table edit alone while finding the fix |
| E5 | Not needed |
