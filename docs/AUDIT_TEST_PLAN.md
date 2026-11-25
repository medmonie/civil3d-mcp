# Audit fixes: one-pass test plan

Build all three plugin targets (2024 / net48, 2025 / net8, 2027 / net10) and the server first. Use a scratch drawing in
metres with one TIN surface, one alignment, one gravity network and one pressure network. Tick each line once.

## A. Transport and protocol (server)

| # | Do | Expect |
|---|----|--------|
| A1 | Any edit on a drawing that has not been granted yet; leave the Yes/No dialog open for 2 minutes, then click Yes | The client keeps waiting (progress every 10 s) and gets the result; no timeout |
| A2 | While A1's dialog is open, send a `ping` from the same client | `ping` answers at once |
| A3 | While A1's dialog is open, send a second edit (another client or request) | Refused with "Another MCP edit is still running"; nothing changed; resend later with the same request_id works |
| A4 | `edit` with `dry_run: true`, then the same edit with the same request_id and `dry_run: false` | Both succeed; the second one applies |
| A5 | Stop Civil 3D, call any read | Clear "Civil 3D is not reachable" message, not a TaskGroup error |
| A6 | Run `MCPSTATUS` repeatedly while a client calls reads in a loop | No "Cannot read the bridge file" errors |
| A7 | `check_default_names` on a drawing with more than 500 alignments (or lower the plugin limit) | `totals` show the real counts; `truncated` appears when cut |

## B. Imports

| # | Do | Expect |
|---|----|--------|
| B1 | WaterGEMS build, then the same call again with the same request_id | Second call returns the recorded result, or the "reply has expired" note; never a KeyError |
| B2 | Start a SewerGEMS build and, while it runs, send the same call again | Second call says `pending: true`, not "networkCommitted: false" |
| B3 | WaterGEMS import where a pipe label already exists as a pressure part name in the drawing | A warning names the part Civil 3D renamed |

## C. Edit operations changed by the patch

| # | Do | Expect |
|---|----|--------|
| C1 | `entity_copy` of a block reference with attributes, and of a 3D polyline | Copies keep their attributes and vertices |
| C2 | `entity_copy` of an entity on a paper-space layout | The copy is on the same layout, not in model space |
| C3 | `block_define` from a block reference with attributes plus a 2D heavy polyline, `replace_originals: true`, entities on a layout | The block contains both intact; the new reference is on that layout |
| C4 | `part_swap` with only `network` + `family` | Refused: "give 'names' or 'handles'"; nothing changed |
| C5 | `part_swap` with a handle from another network | Refused: "is not a part of network" |
| C6 | `feature_line_edit` with `elevations: [{index: "2", elevation: 10}]` and with `index` missing | Refused with a clear message (was: silently wrong point / API error) |
| C7 | `corridor_feature_line_export` with `index: 1.5` | Refused: "must be a whole number" |
| C8 | `points_export` / `points_import` / `inspect_file` / `block_import` with a relative path such as `out.txt` | Refused: "must be a full path" |
| C9 | `inspect_file` on a title-block drawing whose block has two attributes with the same tag | Succeeds |
| C10 | `parts_to_section_views` on a large network (hundreds of parts, dozens of views) | At most 200 warnings plus one "... more warning(s) not listed" line |
| C11 | `pipe_ground_profile` with `interval: 0.001` | Returns quickly; at most 2000 samples per pipe |
| C12 | `sample_surface` along a long line with `interval: 0.0001` | Refused with "use a larger interval" |
| C13 | `sample_lines_create` with an interval that would exceed 5000 lines | A warning says where it stopped |

## D. Behaviour to confirm (not changed by the patch; decides the next fix)

| # | Do | Record |
|---|----|--------|
| D1 | Metric drawing: `pipe_set` with `inner_diameter: 300` | Which size the pipe gets. If it jumps to the largest size, the value is taken in metres (catalogue says mm) |
| D2 | `pressure_network_erase` on a network with parts | Are the pipes/fittings/appurtenances gone, or left orphaned? |
| D3 | Batch: `text_style_create` (new style, `set_current: true`) then an op that fails | After rollback, is the current text style still valid (`list_cad_styles` text_style `current`)? Repeat for dim / mleader style and `annotation_scale_add` |
| D4 | `block_import` with `replace: true` from a file whose layer "0"/"Defpoints"/a shared layer has another colour | Did the drawing's layer colours or text styles change? |
| D5 | `assembly_create` with BasicLane + BasicShoulder on the right | Are they chained (shoulder at lane edge) or both at the baseline point? |
| D6 | `network_interference` with only one pressure network | Are connected pipes at fittings reported as pairs? |
| D7 | `surface_solids_create` with `file` + `overwrite: true` where creation fails (e.g. depth on an empty surface) | Is the old file gone? |
| D8 | Each of `labels_add`, `network_styles` (with label styles), `pressure_labels_add`, `section_views_create`, `section_volume_tables` twice | Duplicates created on the second run? |

## E. Save failure ("object cannot be saved ... no file was created")

Seen once when quitting Civil 3D 2027 after the 0.15-0.19 live suites had all run in one drawing. `diagnose_save` (new,
read only) checks the header's current styles, writes every object through a probe copy filer, and
writes a copy to %TEMP% (deleted again). `tests\run_save.bat` replays the five suites in the same order and runs that
check after every edit.

| # | Do | Expect / record |
|---|----|--------|
| E1 | New drawing from the metric template, `MCPALLOW`, run `tests\run_save.bat` | `tests\results\save.txt` ends with `SAVE CHECK CLEAN` or `SAVE CHECK FOUND THE CAUSE` |
| E2 | If FOUND: read the `FIRST SAVE PROBLEM` block | Suite, edit number and title, its operations, whether it was applied or rolled back, and the objects (handle, type, owner, Civil 3D name) that fail to write or point at erased objects; send me `save.txt` and `save_check.json` |
| E3 | Then QUIT and answer No | Does the "cannot be saved" message appear? |
| E4 | Confirm: new drawing, `run_save.bat --suites <that suite>` | The same edit reproduces it in a fresh drawing |
| E5 | If CLEAN but E3 still shows the message: rerun with `run_save.bat --copy-each-edit` | Whether the real %TEMP% copy fails at some edit although every object writes |
| E6 | Before any test, in an empty drawing: `diagnose_save` | `savable: true` (baseline; otherwise the template is the cause) |

Watch for: a header row such as `TEXTSTYLE=erased` points at H8 (`set_current` of a style created in a batch that
was rolled back); a `danglingHard` row names the object that still points at something erased.
