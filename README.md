# Civil 3D MCP bridge

**Version 0.9 · Autodesk Civil 3D 2027 · Windows**

Lets Claude read the drawing that is open in Civil 3D on this computer and, after you allow it once, edit it:
layers and AutoCAD drafting, surfaces, alignments, profiles, corridors, gravity and pressure pipe networks,
saving and plotting to PDF.

```
Claude ──stdio──> MCP server (c3d-mcp-server.exe, or python server\server.py)
                      │  127.0.0.1 only, random port + per-session token
                      v
                  C3dMcpBridge.dll inside Civil 3D ──> Civil 3D .NET API
```

Nothing leaves the computer: the server only talks to the Civil 3D running on the same machine.

## Contents

1. [Install](#1-install)
2. [Permissions](#2-permissions)
3. [How to use it](#3-how-to-use-it)
4. [Features](#4-features)
5. [What it cannot do](#5-what-it-cannot-do)
6. [Safeguards and limits](#6-safeguards-and-limits)
7. [Troubleshooting](#7-troubleshooting)
8. [For developers](#8-for-developers)

---

## 1. Install

### Option A - installer zip (any PC, recommended)

Needs only Civil 3D 2027 and the Claude desktop app. No Python, no .NET SDK.

1. Close Civil 3D.
2. Unzip `C3dMcp-2027.zip` and double-click **`install.bat`**. It copies
   - the plugin to `%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle` (Civil 3D loads it by itself),
   - the server to `%LOCALAPPDATA%\C3dMcpBridge\c3d-mcp-server.exe`,
   - and prints a configuration block (also saved next to the server as `claude_desktop_config_snippet.json`).
3. In Claude: **Settings > Developer > Edit Config**. Add the block to `claude_desktop_config.json`:
   ```json
   {
     "mcpServers": {
       "civil3d": { "command": "C:\\Users\\<you>\\AppData\\Local\\C3dMcpBridge\\c3d-mcp-server.exe" }
     }
   }
   ```
   If the file already has an `mcpServers` section, add only the `"civil3d": { ... }` line inside it.
4. Restart Claude (quit it completely, then start it again).
5. Start Civil 3D and open a drawing. If Civil 3D asks whether to load `C3dMcpBridge.dll`, choose **Always Load**.
6. Type **`MCPSTATUS`** in the Civil 3D command line. It answers with the bridge version and port.
7. In Claude ask: *"Ping Civil 3D and tell me which drawing is open."*

### Option B - from this source folder (the development PC)

Needs the .NET 10 SDK and Python 3.10 or newer.

1. One time, create the Python environment:
   ```
   python -m venv server\.venv
   server\.venv\Scripts\pip install -r server\requirements.txt
   ```
2. Close Civil 3D and double-click **`build.bat`**. It builds the plugin and copies it to the Civil 3D plugin folder.
   Open `build_result.txt` and check for `0 Error(s)`.
3. Point Claude at the server script (**Settings > Developer > Edit Config**), then restart Claude:
   ```json
   {
     "mcpServers": {
       "civil3d": {
         "command": "C:\\path\\to\\civil3d-mcp\\server\\.venv\\Scripts\\python.exe",
         "args": ["C:\\path\\to\\civil3d-mcp\\server\\server.py"]
       }
     }
   }
   ```
4. Continue with steps 5 to 7 of option A.

`build_package.bat` produces the installer zip of option A in `dist\`.

### Updating

| You changed | Do this |
|---|---|
| The plugin (new version installed or built) | Restart Civil 3D. It keeps using the DLL it loaded at start-up. |
| The server (new exe, or `server\*.py`) | Restart Claude. It reads the list of actions when it starts the server. |

> **Note.** After an update, restart **both** if Claude says an action is unknown: one side is still the old version.
> `action="help"` reports any mismatch between the two under `plugin`.

### Uninstall

Run `uninstall.bat` from the zip, then remove the `"civil3d"` entry from Claude's config.

---

## 2. Permissions

You are asked once in each program, and never per edit.

| Where | What you are asked | How often |
|---|---|---|
| Claude | Allow the tool `civil3d` | Once ("always allow") |
| Civil 3D | "Allow Claude to edit drawings in this session?" | Once per Civil 3D session, at the first edit |

- **Reading never asks anything** in Civil 3D.
- After **Yes**, no later edit asks again, in any drawing, until Civil 3D is closed.
- After **No**, nothing is changed, and edits are refused for 30 seconds so that a retry cannot nag you.

Commands in the Civil 3D command line:

| Command | Effect |
|---|---|
| `MCPSTATUS` | Shows version, port and whether editing is allowed. With several Civil 3D windows open, makes **this** one the one Claude talks to. |
| `MCPALLOW` | Allows editing for this session without the dialog. |
| `MCPREVOKE` | Switches editing off again. Reading keeps working. |

> **Note.** The permission is deliberately not remembered between Civil 3D sessions.
> If you want Claude to read but never edit from the Claude side as well, set the environment variable
> `C3D_MCP_SPLIT=1` for the server: Claude then shows two tools, `civil3d_read` and `civil3d_edit`, that you can allow separately.

---

## 3. How to use it

Open the drawing in Civil 3D, then ask Claude in plain language. Claude chooses the actions itself.

**Typical requests**

| You ask | What happens |
|---|---|
| "What is in this drawing?" | Reads drawing info, layers, surfaces, alignments, networks. |
| "List the pressure pipes of network W-01 with diameter and length, and total the length per diameter." | Reads the pipes and sums them. |
| "Check the layer names against `C-[A-Z]+-[A-Z]+`." | QA check, lists the layers that do not match. |
| "Create layer C-WATR-PIPE, colour 5, and move the selected objects to it." | Select the objects in Civil 3D first; one edit batch. |
| "Build a surface EG from these points ... and a profile of alignment RD-01 on it." | Creates the surface and the surface profile. |
| "Create a pressure network W-02 from parts list Water: DN150 ductile pipes through these coordinates, a 90 degree elbow at each bend." | Adds pipes and fittings and connects them. |
| "Add a manhole every 40 m along this line and connect them with 300 mm concrete pipes." | Gravity network with structures and pipes. |
| "Create sample lines every 20 m on RD-01 and draw the section views." | Sample lines, then a grid of section views. |
| "Plot the current layout to `C:\Projects\out\plan.pdf` on A1, monochrome." | Plots with DWG To PDF. |

**Good habits**

- **Name things.** Civil 3D objects are addressed by name (surface, alignment, network). Plain entities and network
  parts are addressed by their *handle*, which Claude gets from the read actions.
- **Select, then ask.** "the selected objects" works: Claude reads your current selection.
- **Ask for a dry run** before a large edit: *"do a dry run first"*. Everything is executed and rolled back, so you see
  what would happen and any error, with no change to the drawing.
- **One request = one undo step.** An edit batch is a single transaction: if any step fails, nothing is changed;
  if it succeeds, one `Ctrl+Z` in Civil 3D takes all of it back.
- **Units** are the drawing's units. Coordinates are easting, northing, elevation. Pressure pipe diameters are read in
  metres; gravity part sizes in the units of the parts list (mm in metric lists).
- **Parts** come from the drawing's parts lists. A pressure part is chosen by `size` (the number in its description,
  e.g. 150) and `match` (words in it, e.g. "ductile"); a gravity part by `family` and `inner_diameter`. If Claude says
  several parts match, give one more word.

**What a call looks like** (you normally never write this; it helps when reading Claude's tool calls)

```json
{ "action": "list_pressure_pipes", "params": { "network_name": "W-01" } }

{ "action": "edit",
  "params": {
    "dry_run": false,
    "operations": [
      { "op": "layer_create", "name": "C-WATR-PIPE", "color": 5, "if_exists": "skip" },
      { "op": "pressure_network_create", "name": "W-02", "parts_list": "Water", "reference_surface": "EG" },
      { "op": "pressure_add", "network": "W-02",
        "pipes": [ { "start": [10, 90, 8.5], "end": [45, 90, 8.5], "size": 150, "match": "ductile" } ] }
    ] } }
```

---

## 4. Features

One tool, `civil3d(action, params)`: 40 read actions, 5 QA / help / import actions, and 68 edit operations inside the
single `edit` action. Every parameter is listed in **[docs/ACTIONS.md](docs/ACTIONS.md)** and by `action="help"`.

### Read (never changes the drawing)

| Group | What Claude can read |
|---|---|
| Drawing (AutoCAD) | Drawing info, layers, entity counts by type or layer, your selection, objects by handle, entities with geometry (lines, polylines, circles, arcs, text, blocks with attributes, hatches, dimensions), blocks, layouts |
| Terrain | Surfaces and their elevations, elevation at a point, cut / fill of a volume surface, COGO points, feature lines, parcels |
| Alignments | Alignments, station / offset to point and back, profiles, profile elevation at a station, profile views, sample lines, section views |
| Corridors | Corridors with baselines, regions and corridor surfaces; assemblies with their subassemblies |
| Gravity networks | Networks, pipes (size, slope, length, connections), structures (rim, sump), parts lists with families and sizes |
| Pressure networks | Networks, pipes (diameters, lengths, cover), fittings, appurtenances, pipe runs, parts lists and sizes |
| Styles and meta | Style names of each kind (including band and label styles), what a style contains (`get_style`), edit-permission state, API introspection |
| QA | Layer names against a naming pattern; objects still carrying Civil 3D's default names |
| Import | Check a Bentley SewerGEMS export (see below) |

### Edit (after the one-time permission)

| Group | What Claude can do |
|---|---|
| Layers and existing objects | Create and change layers; set layer, colour, linetype; move, copy, rotate, scale, erase; change text; rename / describe / restyle Civil 3D objects |
| Drawing (AutoCAD) | Lines, polylines (2D and 3D), points, circles, arcs, text, multiline text, hatches, dimensions (aligned, horizontal, vertical), multileaders, tables |
| Blocks | Insert blocks with attribute values, define a block from entities, change attribute values |
| Drawing file | Create layouts, attach xrefs, save / save as / save a copy / export DXF, plot a layout or model space to PDF |
| Terrain | COGO points (add, change, remove); TIN surfaces from points, breaklines (from points or from feature lines), boundaries, rebuild; volume surfaces with cut / fill; feature lines |
| Alignments | Alignments from PIs with curves; layout profiles from PVIs; surface profiles; profile views (with network parts); sample lines; section views |
| Corridors | Assemblies from stock subassemblies; corridors (alignment + profile + assembly); corridor surfaces; rebuild |
| Gravity networks | Create a network; add structures and pipes (connected automatically); change a pipe or a structure (rim, sump); labels; add a catalogue family to a parts list; import a SewerGEMS network |
| Styles and presentation | Create label styles (text, flow arrow); restyle and label a whole network; replace alignment labels; profile view style and ranges; profile view bands; band titles and sizes |
| Pressure networks | Create, change, erase a network; add pipes (straight or curved), fittings and valves (connected automatically); pipe runs at a depth of cover with automatic bends; move or resize a part; labels |

---

### Import from Bentley SewerGEMS

Ask Claude, for example: *"Check the SewerGEMS export in `C:\...\sewer network\Full output from sewergems.xlsx` with the
rename table `Output Of Lisp.csv`, then build it."*

| Step | Action | What happens |
|---|---|---|
| 1 | `sewergems_check` | Reads the export and reports manholes, conduits, drop manholes, the profile lines it would make, and any problem: a conduit pointing to a missing manhole, a pipe flowing uphill, a crown above ground, a table length that does not match the manhole positions, a manhole with two outlets. Nothing is drawn. |
| 2 | `sewergems_import` | Builds it in the open drawing (see below). Refuses if step 1 found problems. |

**Input.** The SewerGEMS *full output* workbook (`.xlsx`, sheets `Manhole`, `Outfall`, `Conduit`). FlexTable CSVs also
work if they include the X and Y columns. Optional: a two-column CSV that renames labels (`MH-19,MH-1-19`).

**What is built.**

- **A gravity pipe network.**
  - Manholes are named after SewerGEMS, or after the rename table, with rim = ground level and sump = invert.
  - Pipes keep the SewerGEMS inverts at both ends, so drop manholes are kept.
  - Pipes are oriented by flow: SewerGEMS's `Is Reversed?` flag is honoured.
  - Pipe sizes: the SewerGEMS bore (e.g. 191 mm) is matched to the catalogue's nominal size (PID 200 mm PVC).
- **One alignment per line.** The line runs from its downstream manhole upstream, so chainage 0 is at the outfall or junction. The trunk is `LINE1`; each branch is `LINE1-<junction>`, e.g. `LINE1-3` from MH-1-3.
- **A ground profile for each line** through the manhole rims.
- **A profile view for each line** showing that line's pipes and manholes.

**Presentation** (skip with `presentation: false`, or override any item):

| Where | Result |
|---|---|
| Plan | Pipes in *Single Line (Sanitary)*, manholes in *Sanitary Sewer Manhole*. Each pipe gets a label with a flow arrow and `ø200 L=22m`, each manhole its name. Alignments get no labels and sit on layer `SEWER-ALIGNMENT`, which is turned off. |
| Profile views | *Profile View* style, stacked under the plan. |
| Band table | Drawn under each profile view like the LISP sheets, on layers `EN-P-TXT` and `EN-P-GRID`. Rows: ground level, invert level (two values at a drop manhole), pipe depth, distance (segment lengths and chainage), slope ‰, pipe diameter and pipe material, with one value per run of equal pipes. |

> **Note.** The band table is drawn geometry built from the SewerGEMS data, not a Civil 3D band. The .NET API cannot
> define what a band style writes at each station, and the template's pipe-network bands do not fit a metric sewer
> sheet. Re-import after a design change, or build your own band styles once in your template and set them with
> `profile_view_bands`. The pipe label reads the pipe's *Description*, which the import fills with the nominal size,
> because Civil 3D offers no inner-diameter field for pipe labels.

> **Note.** Civil 3D's coned manholes cannot be shallower than about 1.7 m (frame + cone), and Civil 3D silently raises
> the rim of a shallower one. The importer therefore uses a flat-topped manhole (`Cylindrical Junction Structure NF SI`,
> 1200 mm) and adds it to the parts list if missing. Any manhole whose levels Civil 3D still changed is listed in the
> reply (`levelMismatches`). Laterals and property connections are not imported.

## 5. What it cannot do

| Not possible | Why | What to do instead |
|---|---|---|
| Create parcels | The Civil 3D .NET API can only read them | Create them in Civil 3D; Claude can list them |
| Create grading objects (daylight slopes) | Not exposed by the API | Create a feature line and add it to a surface as a breakline: this gives the pad or channel surface, without automatic daylighting |
| Corridor targets (daylight to a surface, offset alignments), several regions | Not built yet | Set targets in Civil 3D after Claude creates the corridor |
| Section-view sheets and bands, plan production | Not built yet | Views are placed in a grid with default styles |
| Create object styles, band styles, parts lists from scratch | Band contents are not exposed by the API; the others are not built yet | Use the styles of your template by name. Label styles can be created (`label_style_create`), and catalogue families can be added to a parts list |
| Civil 3D 2025 / 2026 | The plugin is built for 2027 (.NET 10) only | - |
| Run AutoCAD commands or scripts | Left out on purpose (no arbitrary code execution) | - |

Also good to know:

- When you name no style, an edit uses the style called "Standard" if the drawing has one, otherwise the first style of
  that kind. Say the style name to get another one ("list the profile view styles" shows them).
- Civil 3D reports rejected breaklines (for example one crossing a surface point at another level) only in its
  Event Viewer, not to Claude.
- The plugin is not code-signed, so Civil 3D asks once whether to load it.

---

## 6. Safeguards and limits

- **Local only.** The plugin listens on `127.0.0.1` on a random port. Each Civil 3D session writes a new random token to
  `%LOCALAPPDATA%\C3dMcpBridge\bridge.json`; a request without it is refused.
- **All or nothing.** One edit request = one transaction. A failing step rolls the whole batch back.
- **Audit log.** Every permission decision and every edit batch is written to `%LOCALAPPDATA%\C3dMcpBridge\audit.log`.
- **Nothing is forced.** Objects on locked layers or in xrefs make the batch fail with a message.
- **Overwrite protection.** Saving a copy, exporting or plotting onto an existing file needs `overwrite: true`.
- **Sanity limits.** 500 operations per batch; coordinates beyond 1e8 (plan) or 1e5 (elevation) are rejected as unit mistakes;
  requests up to 4 MB; lists are cut at their `limit` and say so (`truncated: true`).
- **Busy Civil 3D.** A request waits up to 30 seconds for Civil 3D (a running command or an open dialog blocks it) and is
  then cancelled before it starts. An edit that has started is always finished, never abandoned half-way.

> **Note.** The Yes / No dialog stops the tool from approving itself. It cannot stop a Claude session that also
> controls your screen from clicking "Yes", so only give such a session edit work you would approve yourself.

---

## 7. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| "Civil 3D is not reachable" | Civil 3D is not running, or the plugin did not load. Type `MCPSTATUS` in Civil 3D. If the command is unknown, the plugin is not loaded: check that `%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle` exists and restart Civil 3D; answer the load question with *Always Load*. |
| "Could not reach Civil 3D on port ..." | That Civil 3D was closed, or two are open. Type `MCPSTATUS` in the one you want. |
| "Unknown action" / "Unknown edit operation" | Claude is still running the old server. Restart Claude completely. |
| `help` shows `missingInPlugin` | Civil 3D is still running the old plugin. Restart Civil 3D. |
| "Civil 3D did not become available within 30 s" | A command or dialog is open in Civil 3D. Finish or cancel it (Esc) and ask again. |
| "Editing was declined in Civil 3D a moment ago" | You (or a previous request) answered No. Wait 30 seconds, or run `MCPALLOW`. |
| The edit request seems to hang | The permission dialog is waiting in Civil 3D, possibly behind another window. |
| "N parts match size=..." | Several catalogue parts fit. Add a word to `match` (e.g. "ductile", "90 degree"). |
| A structure ignores the rim / sump you gave | Should not happen from v0.5 on; update the plugin. |
| `build.bat` reports errors about duplicate definitions | Old source files are still present after an update. Run `archive_old_files.bat` once, then `build.bat`. |
| The new plugin is not picked up after `build.bat` | Civil 3D was open during the build. Restart it. |

Where to look: `build_result.txt` (build), `tests\results\` (tests), `%LOCALAPPDATA%\C3dMcpBridge\audit.log` (what was edited).

---

## 8. For developers

### Folder layout

```
plugin/                     the Civil 3D plugin (C#, .NET 10)
  Plugin.cs                 entry point, commands MCPSTATUS / MCPALLOW / MCPREVOKE
  Bridge/                   socket server, marshalling onto Civil 3D's main thread
  Core/                     Registry (the table of actions), J (parameter reading), Db (drawing helpers),
                            Find (objects by name), Styles, PointIndex, WriteGate (permission), Audit
  Parts/                    choosing a gravity / pressure part from a parts list
  Read/                     read actions:      Drawing, Terrain, Alignment, Corridor, Gravity, Pressure, Style, Api
  Edit/                     EditEngine (one transaction per batch), EditContext, and the operations:
                            Object, Draw, Block, Document, Terrain, Alignment, Corridor, Gravity, Pressure
  Ui.cs                     the only Windows Forms code (the permission dialog)
server/                     the MCP server (Python)
  server.py                 the single tool and its dispatch
  catalog.py                names, descriptions and parameters of every action (what Claude sees)
  bridge_client.py          talks to the plugin
  qa.py                     QA checks computed from read actions
tests/                      smoke_test.py (no Civil 3D), live_read.py, live_edit.py, fake_bridge.py, *.bat launchers
tools/                      gen_docs.py (docs/ACTIONS.md), typecheck/ (compile without deploying), apitool/ (API signatures)
package/, build_package.bat the installer zip
docs/ACTIONS.md             generated action reference
```

`Read/` and `Edit/` use the same subject names, and `catalog.py` is grouped the same way, so the place of any action
is predictable: `list_pressure_pipes` is in `Read/PressureRead.cs`, `pressure_add` in `Edit/PressureOps.cs`.

### Adding an action

1. **Plugin.** Write the handler in the matching `Read/...Read.cs` or `Edit/...Ops.cs`.
   - Read action: `public static object X(JsonElement p)`, registered in `Core/Registry.cs` with `Read("name", ...)`.
   - Edit operation: `private static object X(EditContext c, JsonElement p)`, registered in the file's own `Register`
     with `ops["name"] = X`. Use `c.Tr` (never start a transaction), `c.Warn(...)` for non-fatal remarks, and
     `c.AfterCommit` for work that must run after the commit (saving, plotting).
   - Read parameters with `J` (`NeedStr`, `DblParam`, `PtList`, `Choice`, ...): they validate and produce the error text.
   - Throw `ArgumentException` for a wrong request; the message goes to Claude as written.
2. **Catalogue.** Add the entry to the same group in `server/catalog.py`.
3. **Check.** `python tools/gen_docs.py`, then `tests\run_smoke_test.bat`. The test fails if the plugin and the catalogue
   disagree, if the version numbers differ, or if `docs/ACTIONS.md` is stale.

### Build and test

| Step | Command | Result |
|---|---|---|
| Build and deploy the plugin | `build.bat` (Civil 3D closed) | `build_result.txt` |
| Offline test of the server | `tests\run_smoke_test.bat` | `tests\results\smoke_test.txt` |
| All read actions, live | `tests\run_live_read.bat` | `tests\results\live_read.txt` |
| All edit operations, live | `tests\run_live_edit.bat` | `tests\results\live_edit.txt`, ends with `ALL PASS` |
| Installer zip | `build_package.bat` | `dist\C3dMcp-2027.zip`, log `package_build.txt` |

> **Note.** `live_edit.py` draws test objects. It refuses to run unless the active drawing is a new unsaved
> `DrawingN.dwg` or its name contains `mcp_test`. Run `MCPALLOW` first, or answer the dialog once.

The version is set in `plugin/BridgeInfo.cs` and `plugin/PackageContents.xml` (the test keeps them equal).
History: [CHANGELOG.md](CHANGELOG.md).

## Licence

Copyright (c) 2026 Mohamed Ahmed Hamed.

This project is **source-available** under the [PolyForm Noncommercial License 1.0.0](LICENSE.md):

- You may read, modify and use it for **non-commercial** purposes (personal projects, study, research, hobby work, and use by charities, schools and public bodies).
- **Commercial use, including use on paid projects and selling, needs written permission** from the copyright holder. Contact the author via GitHub to ask for a commercial licence.

Contributions are welcome through pull requests. By submitting one, you agree that it may be distributed under this licence.
