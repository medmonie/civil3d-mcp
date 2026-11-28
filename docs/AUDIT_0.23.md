# Code audit and coordinate system (0.23.0)

27 Nov 2026. Scope: `server/`, `plugin/`, `package/`, build scripts, as they stood at 0.22.0 in this folder.
Method: full read of the protocol path (server.py, bridge_client.py, qa.py, BridgeServer, MainThread, EditRequests,
EditEngine, EditContext, WriteGate, DrawingTarget, J, FilePolicy call sites) and pattern sweeps over the operation
and read files (file access, loops, deferred LINQ, swallowed exceptions, integer parameters, catalogue against
handlers). The operation files were not read line by line.

## Executive summary

**Verdict: fit for use on the workstation it was built for; not ready to hand to other offices without 0.23.**
No Critical defect. Three High defects were confirmed and fixed. The earlier audit's open High items H4 (file
boundary) and H7 (work budget) are closed or reduced; H5 and H8 were already fixed in this folder.

Top blockers found:

1. A failed edit could reach the client as a **success** on MCP SDK 1.9 - 1.18, which `requirements.txt` allowed.
2. `points_export` could write client-controlled text with **any extension into any folder**: a Startup script,
   the audit log, `bridge.json`.
3. One failed socket accept **stopped the bridge for the rest of the Civil 3D session** while it still reported
   itself as listening.

**Verification.** Plugin: built on the laptop for Civil 3D 2024 (net48), 2025 (net8) and 2027 (net10), 0 errors, no
warnings in the changed files. Live on Civil 3D 2027: `tests\run_v023.bat`, 56 checks, ALL PASS. Server: `tests\smoke_test.py` ALL OK
on MCP SDK 1.30.0 (now includes `policy_test.py`, which fails on the 0.22 code as intended). Not verified live:
2024 and 2025 (compiled only), the accept-loop fix and the commit-failure receipt (no way to provoke them on demand),
the permission retry after a real "No" in the dialog.

## Findings

Confirmed = reproduced or read directly in the code path. Each fixed item names the file that carries the fix.

### High

**H-1 Failed edits reported as successes on older MCP SDKs** (confirmed by test) - `server/server.py` `_write_result`,
`server/requirements.txt`
- Wrong: `_write_result` returns a `CallToolResult(isError=True)`. I ran the same tool on SDK 1.9, 1.12, 1.15, 1.17
  and 1.18: every one serialised that object as plain text and answered `isError=false`. Only 1.19+ passes it
  through. `mcp[cli]>=1.9,<2` therefore allowed installs on which a denied or rolled-back edit looks successful.
  The installed venv has 1.30.0, so the shipped exe was not affected.
- Fix: `mcp[cli]>=1.19,<2`; `_require_sdk()` refuses to start on an older SDK (message on stderr, exit 2).

**H-2 `points_export` wrote any extension to any folder** (confirmed) - `plugin/Edit/GeometryOps.cs`
- Wrong: the path only had to be absolute. The file content is point data the client controls (descriptions), so
  once editing was allowed a prompt-injected client could drop a `.bat` in Startup, or overwrite
  `%LOCALAPPDATA%\C3dMcpBridge\audit.log` (the record of its own actions) or `bridge.json`.
- Fix: data extensions only (.txt .csv .prn .pnt .xyz .nez .auf .asc .dat); every write goes through
  `FilePolicy.Write`. Live: all five attack paths refused at the operation, audit log intact.

**H-3 Accept loop ended on the first failed accept** (confirmed in code; trigger is conditional) -
`plugin/Bridge/BridgeServer.cs` `AcceptLoop`
- Wrong: `catch { break; }` treated every exception as shutdown. Windows reports a client that resets before it is
  accepted on the listening socket. After that nothing accepts connections, yet `bridge.json` and MCPSTATUS still
  advertise the port until Civil 3D restarts.
- Fix: leave the loop only on cancellation or after `Stop()`; otherwise back off (50 ms to 2 s) and continue.
  MCPSTATUS shows a listener that keeps failing.

### Medium

**M-1 "Permission not granted" was stored as the edit's final result** - `plugin/Bridge/EditRequests.cs`
- Wrong: the refusal receipt was recorded as `completed`, so the same `request_id` answered "not granted" for ever,
  even after MCPALLOW. Nothing had run.
- Fix: recorded as `error` with `retryable: true`; the same request can be sent again.

**M-2 A commit that throws left no receipt and skipped clean-up** - `plugin/Edit/EditEngine.cs`
- Wrong: an exception from `tr.Commit()` escaped `RunExclusive`: no `applied:false` receipt, no audit line, and
  `RunAfterEnd` (which releases objects Civil 3D leaves open) never ran.
- Fix: commit failure returns a rolled-back receipt (`failedAt: "commit"`), is audited; `RunAfterEnd` is in `finally`.

**M-3 Unbounded sampling on Civil 3D's main thread** (earlier H7) - `plugin/Read/GeometryRead.cs`
- Wrong: `(int)Math.Ceiling(len / step)` with a caller-chosen `step`: a tiny step froze Civil 3D; a ratio beyond
  `int` wrapped to one sample, a silently wrong volume.
- Fix: 2,000,000 samples per material, checked before the cast, with a clear error.
- Still open: `network_interference` keeps every hit before applying `limit`.

**M-4 Whole numbers written by edits were silently changed** (earlier M3) - `plugin/Core/J.cs` and six call sites
- Wrong: `IntParam` clamps and falls back to the default. `"decimals": "3"`, `2.5` or `12` all produced a dimension
  style nobody asked for; same for `per_row`, `columns`, `count` and two colour indexes.
- Fix: `WholeParam` refuses text, fractions and out-of-range values. `IntParam` stays for `limit`-type reads.

**M-5 Cancelled reads kept their thread; reply timeout was per recv** (earlier H2 / M7) - `server/bridge_client.py`
- Wrong: a cancelled read sat on the socket for up to 90 s. A reply arriving a byte at a time never timed out:
  on the 0.22 code a 1.5 s timeout did not end a reply sent one byte every 0.2 s.
- Fix: `_read_reply` uses one monotonic deadline; reads are abandoned within 0.5 s of cancellation; edits never are.

**M-6 No file boundary** (earlier H4) - new `plugin/Core/FilePolicy.cs`, `server/filepolicy.py`
- Wrong: any path the Windows user could reach, on five separately written checks.
- Fix: one policy on both sides. Always: full paths, no device paths or alternate streams, nothing in the bridge
  folder, no writes under Windows / Program Files / Startup / Autodesk plug-in folders, never an executable or
  script type. Optional `policy.json` with `allowedRoots` limits everything to project folders (fails closed when
  unreadable). `get_edit_permission` reports it.
- Residual: without `policy.json`, reads of any drawing or workbook the user can open remain possible. The plugin
  side does not resolve junctions inside an allowed root.

**M-7 Unbounded workbook, CSV and regex work** (earlier M6) - `server/filepolicy.py`, `server/qa.py`
- Fix: 64 MB and 200,000 rows per input; layer pattern 300 characters.
- Residual: Python's `re` cannot be time-limited; a pathological short pattern can still hold a worker thread.

**M-8 Callers without the token could hold connection slots** (earlier M7) - `BridgeServer.cs`
- Fix: an unauthorized or malformed request gets one answer and the connection is closed.

### Low (fixed)

- `BridgeServer.Start`: a `TcpListener.Start()` failure left `_listener` set, so a later `Start()` did nothing.
- `EditEngine`: its "busy" error told the caller to resend with the same ID, but the ledger would have marked it
  non-retryable. Unreachable today (the ledger refuses first); now a typed `EditBusyException`.
- `linetype_load`: `file` accepted relative paths with folders. Workbook paths accepted relative paths (resolved
  against the server's working folder).

### Open, not changed (your decision or outside this pass)

| Item | Why it is open |
|---|---|
| Tool description is 43.7 KB (about 11,000 tokens) in every session | Structural: one tool carries every signature. Moving signatures behind `help` changes what clients see. |
| Edit ledger stops at 8,192 edits per session, then needs a restart | Deliberate (earlier H1); a per-drawing persistent ledger would remove it. |
| Two Civil 3D instances starting at once, PID reuse | Earlier M8, unchanged. |
| `show_objects` in the read-only tool changes view and selection | Earlier M9, unchanged. |
| Abandoned UI callbacks under repeated busy pings | Earlier M10, unchanged. |
| SDK lifecycle and JSON-RPC error codes (earlier M1, M2) | SDK behaviour; not re-tested in this pass. |
| `configure_clients.ps1`: Claude config written in place; Codex inline-table form of `mcp_servers.civil3d` not recognised (a second table would be appended) | Not hit by a default install. |
| Plugin DLL is unsigned: Civil 3D asks "Load Once / Always Load" at every new build | Roadmap #22. |

Earlier H5 (vertical curves) and H8 (client settings on upgrade) are fixed in this folder: H8 read in
`configure_clients.ps1`; H5 per CHANGELOG 0.22 and `tests\results\v022.txt`, not re-read.

## Coordinate system (roadmap #39)

Three reads and one edit; API identical in the 2024, 2025 and 2027 assemblies (checked in their metadata).

- `get_coordinate_system`, `list_coordinate_systems`, `transform_coordinates`, `coordinate_system_set`.

The live test found four defects in my first version, all fixed before this report:

1. `SettingsDrawing.TransformationSettings` throws on a drawing whose transformation tab was never filled in; it
   failed the whole read. Now that section reports `available: false` with the reason.
2. The library lookup is case-sensitive (`utm84-36n` was refused). Codes are now matched in any case.
3. Civil 3D refuses an empty code; "no coordinate system" is the code `.`.
4. Set inside the transaction, a dry run or failed batch restored the code but left AutoCAD's geographic location
   converting with the rolled-back zone (33 E came back as 27 E). The assignment now runs after the commit, like
   save and plot; a dry run only validates.

Limits: transformation settings are read only. After clearing, AutoCAD keeps its location object without a system;
`transform_coordinates` refuses it rather than return numbers.

## Redundancies and cleanup (behaviour-preserving, not applied)

- "Must be a full path with one of these extensions" is still written five times (`J.NeedFullPath`,
  `DocumentOps.FullPath`, `BlockOps.ReadSide`, `CadOps.UnderlayAttach`, `plot_pdf` folder). All now call
  `FilePolicy`; fold the extension list into `FilePolicy.Read/Write(path, op, extensions)`.
- "Exists and no `overwrite`" is repeated in `DocumentOps.CheckWritable`, `points_export`,
  `surface_solids_create` and the `plot_pdf` folder loop.
- The busy message text exists in both `EditRequests` and `EditEngine`.
- `GravityRead` and `PressureRead` carry the same `Networks` / parts-iteration helpers.
- ACI colour construction is repeated (`EditContext.Aci`, a local in `CivilOps`).
- `ChatGPT audit\` holds copies of six files that are also in `docs\`.
- `CIVIL 3D MCP - ChatGPT\civil3d-mcp` is an older tree (pre-0.22 `AlignmentOps`, `StyleOps`, `DrawingRead`,
  `EditEngine`, `configure_clients.ps1`). Keep one tree and one remote as the source.
- Earlier L3 (`watergems._edit(phase)`, `failed(stage)` unused arguments): carried over, not re-checked.

## Patch (applied in this folder)

New: `plugin/Core/FilePolicy.cs`, `plugin/Read/GeoRead.cs`, `plugin/Edit/GeoOps.cs`, `server/filepolicy.py`,
`tests/policy_test.py`, `tests/v023_live.py`, `tests/run_v023.bat`.

Changed: `plugin/Bridge/BridgeServer.cs`, `plugin/Bridge/EditRequests.cs`, `plugin/Edit/EditEngine.cs`,
`plugin/Core/J.cs`, `plugin/Core/Registry.cs`, `plugin/Core/WriteGate.cs`, `plugin/BridgeInfo.cs`,
`plugin/PackageContents.xml`, `plugin/Read/GeometryRead.cs`, `plugin/Edit/{AlignmentOps,BlockOps,CadOps,CivilOps,
DocumentOps,GeometryOps,ModelOps}.cs`, `server/{server,bridge_client,qa,catalog,sewergems,watergems}.py`,
`server/requirements.txt`, `build_package.bat`, `tests/smoke_test.py`, `docs/ACTIONS.md`, `README.md`,
`ROADMAP.md`, `CHANGELOG.md`.

Not done: `build_package.bat` and `install.bat` (the installed `c3d-mcp-server.exe` is still 0.22, so MCP clients do
not see the new actions yet), the GitHub push, the ChatGPT folder. `tests\x_probe_geo.py`, `tests\run_x_probe.bat`
and `tests\results\x_probe_geo.txt` are a diagnostic probe from this session and can be deleted.
