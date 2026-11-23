# Civil 3D MCP bridge

**Version 0.15 · Autodesk Civil 3D 2024 / 2025 / 2027 · Windows**

Lets Claude, ChatGPT and other MCP clients read the open Civil 3D drawing and edit it after you grant permission in Civil 3D.
It covers:
layers and AutoCAD drafting, surfaces, alignments, profiles, corridors, gravity and pressure pipe networks,
saving and plotting to PDF.

```
Claude / ChatGPT / Codex ─stdio─> MCP server (c3d-mcp-server.exe, or python server\server.py)
                      │  127.0.0.1 only, random port + per-session token
                      v
                  C3dMcpBridge.dll inside Civil 3D ──> Civil 3D .NET API
```

The connection between the MCP server and Civil 3D stays on this computer. Tool results are returned to the
client and can contain drawing names, paths, coordinates, and geometry. ChatGPT in a browser connects through an
optional Secure MCP Tunnel, read-only.

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

Needs Civil 3D 2024, 2025 or 2027 and an MCP client: Claude Desktop, the ChatGPT desktop app, or Codex. The package includes the
MCP server; no Python or .NET SDK is needed. One zip serves all three releases: the bundle holds a plugin DLL per
release and Civil 3D loads the one that matches it.

1. Close Civil 3D.
2. Unzip `C3dMcp.zip` and double-click **`install.bat`**. It copies
   - the plugin to `%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle` (Civil 3D loads it by itself),
   - the server to `%LOCALAPPDATA%\C3dMcpBridge\c3d-mcp-server.exe`,
   - and saves `claude_desktop_config_snippet.json` and `codex_mcp_config_snippet.toml` next to the server.
3. Connect your MCP client (next section).
4. Start Civil 3D and open a drawing. If Civil 3D asks whether to load `C3dMcpBridge.dll`, verify the path and publisher
   details, then choose **Always Load**. Run **`MCPSTATUS`** in the Civil 3D command line.
5. In a chat, ask *“Ping Civil 3D and tell me which drawing is open.”*

### Connect your MCP client

**Claude Desktop.** **Settings > Developer > Edit Config**, then add the block from `claude_desktop_config_snippet.json`
to `claude_desktop_config.json` (merge it into an existing `mcpServers` section if there is one):

```json
{
  "mcpServers": {
    "civil3d": { "command": "C:\\Users\\<you>\\AppData\\Local\\C3dMcpBridge\\c3d-mcp-server.exe" }
  }
}
```

Fully quit Claude (tray icon > Quit) and start it again. `civil3d` appears under the tools menu of a new chat.

**ChatGPT desktop app.** **Settings > MCP servers > Add server**. Name it `civil3d`, choose **STDIO**, and use the full
executable path printed by the installer as the command, with no arguments. If `civil3d` already exists, update its
command. Save and select **Restart** when prompted. In a local chat, `/mcp` shows whether it is enabled.
See [OpenAI’s MCP setup guide](https://learn.chatgpt.com/docs/extend/mcp).

**Codex CLI / IDE.** Add the table from `codex_mcp_config_snippet.toml` to `~/.codex/config.toml` (update the existing
`[mcp_servers.civil3d]` table if present). The ChatGPT desktop app and Codex share this configuration.

### Option B - from this source folder (the development PC)

Needs the .NET 10 SDK (it also builds the .NET 8 and .NET Framework 4.8 plugins; their targeting packs are
downloaded on the first build) and Python 3.10 or newer.

1. One time, double-click **`setup_server.bat`**. It creates `server\.venv` for this PC, installs the requirements
   and runs the offline smoke test; `setup_result.txt` should end with `ALL OK` and `RESULT=DONE`.
   (A `.venv` copied from another PC never works: it points to that PC's Python. The script moves it aside.)
2. Close Civil 3D and double-click **`build.bat`**. It builds the plugin for each Civil 3D release it finds and copies
   it to the Civil 3D plugin folder. Open `build_result.txt` and check for `0 Error(s)` and one
   `Civil 3D 20xx: deployed` line per release (see [Building for 2024, 2025 and 2027](#building-for-2024-2025-and-2027)).
3. Configure `civil3d` in your client with the virtual environment’s `python.exe` as the command and the absolute
   `server.py` path as its single argument. Claude Desktop:
   ```json
   "civil3d": { "command": "C:\\path\\to\\civil3d-mcp\\server\\.venv\\Scripts\\python.exe",
                "args": ["C:\\path\\to\\civil3d-mcp\\server\\server.py"] }
   ```
   ChatGPT desktop / Codex TOML:
   ```toml
   [mcp_servers.civil3d]
   command = 'C:\path\to\civil3d-mcp\server\.venv\Scripts\python.exe'
   args = ['C:\path\to\civil3d-mcp\server\server.py']
   ```
4. Restart the MCP client and continue with steps 4 and 5 of option A.

`build_package.bat` produces, in `dist\`, the installer zip of option A (`C3dMcp.zip`) and a clean source zip
(`C3dMcp-source.zip`) for building on another PC.

### ChatGPT in a browser or cloud chat

Use [Secure MCP Tunnel](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels) to run the stdio server on
the same Windows PC as Civil 3D. The tunnel makes an outbound connection to OpenAI and returns tool results to ChatGPT.
This setup starts the server with `--read-only`, which exposes `civil3d_read` and rejects drawing edits and SewerGEMS / WaterGEMS imports.

1. In [OpenAI Platform tunnel settings](https://platform.openai.com/settings/organization/tunnels), create a tunnel
   associated with your ChatGPT workspace. Obtain a runtime key with tunnel access and download the
   [Windows tunnel client](https://github.com/openai/tunnel-client/releases/latest). The workspace must allow custom MCP servers.
2. Open Civil 3D with a drawing and run `MCPSTATUS`. In PowerShell on the same PC and under the same Windows account,
   run the following with `tunnel-client.exe` on your `PATH`:

   ```powershell
   $serverExe = (Join-Path $env:LOCALAPPDATA 'C3dMcpBridge\c3d-mcp-server.exe').Replace('\', '/')
   $env:MCP_COMMAND = '"{0}" --read-only' -f $serverExe
   $env:CONTROL_PLANE_TUNNEL_ID = Read-Host 'Tunnel ID'
   $runtimeKey = Read-Host 'Tunnel runtime API key' -AsSecureString
   $env:CONTROL_PLANE_API_KEY = [pscredential]::new('tunnel', $runtimeKey).GetNetworkCredential().Password
   try {
       tunnel-client doctor --explain
       if ($LASTEXITCODE -eq 0) { tunnel-client run }
   } finally {
       Remove-Item Env:CONTROL_PLANE_API_KEY, Env:CONTROL_PLANE_TUNNEL_ID, Env:MCP_COMMAND -ErrorAction SilentlyContinue
   }
   ```

3. While the tunnel is running, open **ChatGPT > Plugins > Add custom MCP server**, choose **Tunnel**, and select your
   tunnel. Enable the custom plugin in a chat and ask it to ping Civil 3D. Keep Civil 3D and the tunnel client running
   while using it. [OpenAI’s ChatGPT connection guide](https://developers.openai.com/plugins/deploy/connect-chatgpt)

### Updating

| You changed | Do this |
|---|---|
| The plugin (new version installed or built) | Restart Civil 3D. It keeps using the DLL it loaded at start-up. |
| The server (new exe, or `server\*.py`) | Restart the MCP client or tunnel so it starts the updated server. Refresh the custom plugin’s tools in ChatGPT after a schema change. |

> **Note.** After an update, restart **both** if the MCP client reports an unknown action.
> `action="help"` reports any mismatch between the two under `plugin`.

### Uninstall

Run `uninstall.bat` from the zip, then remove `civil3d` from the MCP client’s settings. For cloud setup, stop the
tunnel runtime and remove the custom MCP server from ChatGPT.

---

## 2. Permissions

The MCP client and Civil 3D manage approvals independently.

| Where | What you are asked | How often |
|---|---|---|
| Claude, ChatGPT or other MCP client | Allow the MCP tool call | Depends on client and workspace settings |
| Civil 3D | Allow the MCP client to edit the target drawing | Once for each open drawing, at the first edit |

- **Reading never asks anything** in Civil 3D.
- After **Yes**, the grant applies to that drawing until it closes or `MCPREVOKE` is run.
- After **No**, nothing is changed, and edits are refused for 30 seconds so that a retry cannot nag you.

Commands in the Civil 3D command line:

| Command | Effect |
|---|---|
| `MCPSTATUS` | Shows version, port and whether editing is allowed. It selects this Civil 3D instance for MCP calls. |
| `MCPALLOW` | Allows editing of the active drawing without the dialog. |
| `MCPREVOKE` | Revokes edit permission for all open drawings in this Civil 3D instance. |

> **Note.** The permission is deliberately not remembered between Civil 3D sessions.
> Add `--read-only` to the server arguments to expose only `civil3d_read`. For local clients that support per-tool
> approvals, `C3D_MCP_SPLIT=1` exposes `civil3d_read`, `civil3d_edit` and `civil3d_import` (SewerGEMS / WaterGEMS imports)
> separately.

---

## 3. How to use it

Open the drawing in Civil 3D, then ask your AI assistant in plain language. It chooses the actions.

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
  parts are addressed by their *handle*, which the assistant gets from the read actions.
- **Select, then ask.** The assistant can read the current selection in Civil 3D.
- **Ask for a dry run** before a large edit: *"do a dry run first"*. Everything is executed and rolled back, so you see
  what would happen and any error, with no change to the drawing.
- **Confirm the drawing before editing.** `get_drawing_info` returns a `target` and a `new_edit_request_id`.
  Pass those as `target` and `request_id` on the edit. Use the target from the drawing you read when preparing the edit.
- **Drawing changes in a batch share one transaction.** A failing drawing operation rolls back the batch. Saving,
  exporting, and plotting run after the drawing transaction commits.
- **Units** are the drawing's units. Coordinates are easting, northing, elevation. Pressure pipe diameters are read in
  metres; gravity part sizes in the units of the parts list (mm in metric lists).
- **Parts** come from the drawing's parts lists. A pressure part is chosen by `size` (the number in its description,
  e.g. 150) and `match` (words in it, e.g. "ductile"); a gravity part by `family` and `inner_diameter`. If the assistant says
  several parts match, give one more word.

**Sheets.** Every draw operation (text, lines, blocks, tables...) takes `layout` to draw on paper space. A typical
sheet: `layout_from_template` (your office template's layout with its title block), `viewport_create` with
`window` or `scale: "1:500"`, `attribute_set` / `title_block_insert` for the title block text, then `plot_pdf` with
`layouts: "all"` for one multi-sheet PDF. Paper space is in millimetres; a 1:500 viewport in a metre drawing shows
0.5 m per paper millimetre.

**What a tool call looks like**

```json
{ "action": "list_pressure_pipes", "params": { "network_name": "W-01" } }

{ "action": "edit",
  "params": {
    "target": { "session_id": "<from get_drawing_info>", "drawing_id": "<from get_drawing_info>" },
    "request_id": "<new_edit_request_id from get_drawing_info>",
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

One tool, `civil3d(action, params)`: 56 read actions, 9 QA / help / import / sheet actions, and 100 edit operations inside the
single `edit` action. Every parameter is listed in **[docs/ACTIONS.md](docs/ACTIONS.md)** and by `action="help"`.

### Read (never changes the drawing)

| Group | What the client can read |
|---|---|
| Drawing (AutoCAD) | Drawing info, layers, entity counts by type or layer, your selection, objects by handle, entities with geometry (lines, polylines, circles, arcs, text, blocks with attributes, hatches, dimensions), blocks, layouts |
| Sheets and set-up | Viewports (scale, model area, twist, frozen layers), named views, xrefs with status, text / dimension / multileader / table styles, linetypes, layer states, annotation scales, page setups, a block's attributes and dynamic properties |
| Terrain | Surfaces and their elevations, elevation at a point, cut / fill of a volume surface, surface analysis ranges and watersheds, COGO points, point groups, point file formats, description keys, feature lines, parcels |
| Alignments | Alignments, station / offset to point and back, profiles, profile elevation at a station, profile views, sample lines, section views, superelevation (curves, critical stations, lane and shoulder slopes), corridor intersections |
| Corridors | Corridors with baselines, regions and corridor surfaces; assemblies with their subassemblies |
| Gravity networks | Networks, pipes (size, slope, length, connections), structures (rim, sump), parts lists with families and sizes, hydraulic grade lines with flooding / surcharge flags, a clearance / clash check between networks (gravity and pressure) |
| Pressure networks | Networks, pipes (diameters, lengths, cover), fittings, appurtenances, pipe runs, parts lists and sizes |
| Styles and meta | Style names of each kind (including band and label styles), what a style contains (`get_style`), edit-permission state, API introspection |
| QA | Layer names against a naming pattern; objects still carrying Civil 3D's default names |
| Import | Check a Bentley SewerGEMS export (see below) |

### Edit (after the one-time permission)

| Group | What the client can edit |
|---|---|
| Layers and existing objects | Create and change layers; set layer, colour, linetype; move, copy, rotate, scale, erase; change text; rename / describe / restyle Civil 3D objects |
| Drawing (AutoCAD) | Lines, polylines (2D and 3D), points, circles, arcs, text, multiline text, hatches, dimensions (aligned, horizontal, vertical), multileaders, tables |
| Blocks | Insert blocks with attribute values (in model space or on a layout), define a block from entities, change attribute values, bring blocks in from another drawing, title blocks, dynamic block properties |
| Drawing file and sheets | Layouts (create, delete, copy from a template with its title block and viewports), page setups, viewports (at a scale, from a window or a named view, twisted, locked, layers frozen per viewport), named views, save / save as / save a copy / export DXF, PDF of one layout, a multi-sheet PDF, or one PDF per layout |
| External references | Attach, reload, detach and bind xrefs; PDF and image underlays |
| AutoCAD styles and set-up | Text, dimension and multileader styles; linetypes; annotation scales; layer states; purge |
| Terrain | COGO points (add, change, remove); import a point file (survey) into a point group; point groups with queries; description keys; TIN surfaces from points, breaklines (from points or from feature lines), boundaries, rebuild; elevation, slope and slope-arrow banding; waterdrop paths; volume surfaces with cut / fill; feature lines |
| Alignments | Alignments from PIs with curves or from a polyline; offset alignments; superelevation slopes at critical stations; layout profiles from PVIs; surface profiles; profile views (with network parts); sample lines; section views |
| Corridors | Assemblies from stock subassemblies; corridors (alignment + profile + assembly); corridor surfaces; rebuild |
| Gravity networks | Create a network; add structures and pipes (connected automatically); change a pipe or a structure (rim, sump); labels; pipe and structure schedules as tables; hydraulic / energy grade lines and flow; add a catalogue family to a parts list; import a SewerGEMS network |
| Styles and presentation | Create label styles (text, flow arrow); restyle and label a whole network; replace alignment labels; profile view style and ranges; profile view bands; band titles and sizes |
| Pressure networks | Create, change, erase a network; add pipes (straight or curved), fittings and valves (connected automatically); pipe runs at a depth of cover with automatic bends; move or resize a part; labels |

---

### Import from Bentley SewerGEMS

Ask the assistant, for example: *"Check the SewerGEMS export in `C:\...\sewer network\Full output from sewergems.xlsx` with the
rename table `Output Of Lisp.csv`, then build it."*

| Step | Action | What happens |
|---|---|---|
| 1 | `sewergems_check` | Reads the export and reports manholes, conduits, drop manholes, the profile lines it would make, and any problem: a conduit pointing to a missing manhole, a pipe flowing uphill, a crown above ground, a table length that does not match the manhole positions, a manhole with two outlets. Nothing is drawn. |
| 2 | `sewergems_import` | Builds it in the open drawing identified by `target`. Pass a unique `request_id` for the import. |

**Input.** The SewerGEMS *full output* workbook (`.xlsx`, sheets `Manhole`, `Outfall`, `Conduit`). FlexTable CSVs also
work if they include the X and Y columns. Use metres for coordinates, lengths and elevations, millimetres for
diameters, and m/km for slopes. The target drawing must use metres in Civil 3D drawing settings. An optional
two-column CSV can rename labels (`MH-19,MH-1-19`).

The network, profile objects, and styles share one drawing transaction. A dry run validates and rolls back that
transaction. View positioning and band tables follow in a separate transaction. If that stage fails, the reply
reports `coreApplied: true`, `built: false`, and the finishing-stage result. After an interruption, use the same
target, import inputs, and request ID to recover a completed stage or continue when finishing had yet to start.
After a confirmed rollback of the finishing transaction, correct the drawing issue and increment `finish_attempt`
while keeping the original import request ID and inputs.

**What is built.**

- **A gravity pipe network.**
  - Manholes are named after SewerGEMS, or after the rename table, with rim = ground level and sump = invert.
  - Pipes keep the SewerGEMS inverts at both ends, so drop manholes are kept.
  - Pipes are oriented by flow: SewerGEMS's `Is Reversed?` flag is honoured.
  - Pipe sizes: the SewerGEMS bore is matched to a metric catalogue size, with up to 5% allowance for nominal sizing
    (e.g. 191 mm to 200 mm). Imports with an unsuitable size are rejected before drawing.
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

### Import from Bentley WaterGEMS

Ask the assistant, for example: *"Check the WaterGEMS export `C:\...\water network\Output from water model.xlsx` against
this drawing (valves on layer Gate-Valve, services on WATER-63 and WATER-25mm, hydrants on FIRE FITTING, drawn pipes on
the WE. P. DI layers), then build it."*

| Step | Action | What happens |
|---|---|---|
| 1 | `watergems_check` | Reads the export and the named CAD layers. Reports pipe lengths by material and size, the parts that will stand in, the valves, service taps and hydrants found in the CAD, the profile lines of the mains, fittings that cannot be placed, and where the model and the drawing differ (pipe routes more than 1 m apart, nodes away from the drawn pipes). Nothing is drawn. |
| 2 | `watergems_import` | Builds it in the open drawing identified by `target`, with a unique `request_id`. |

**Input.** The WaterGEMS *full output* workbook (`.xlsx`, sheets `Pipe` and `Junction`, plus `PRV` and others when
present). Rows repeat per time step; the first row of each element is used, and rows of one element that disagree on
position, elevation, nodes, size or material stop the import. Metres and millimetres; the drawing must
use metres. The node elevation is taken as ground level.

**What is built.**

- **A pressure network** named `Water`. Every pipe keeps its WaterGEMS label; a pipe cut by a valve or a service tap
  becomes `P-438.1`, `P-438.2`, ... Pipes are laid with `cover` (default 1.0 m) to the top of pipe, at the largest
  pipe's depth where several meet.
- **Fittings at the nodes:** a tee where three pipes meet, a bend where the route turns by a catalogue angle (11.25,
  22.5, 45, 90 degrees), a pressure-reducing valve for each PRV (described `PRV 3.5 BAR (PRV-62)`); a gate valve stands
  in when the parts list has no PRV of that size.
- **From the CAD:** a gate valve wherever a valve chamber sits on a model pipe (`valve_layer`), a tee and service pipe
  for each service connection (`service_layers`, size read from the layer name). Hydrants (`hydrant_layer`) are
  listed and kept as the drawn blocks.
- **Plan labels** with the real material and size, one per WaterGEMS pipe (`PVC Ø110 L=59m`), and a label per valve.
- **Profiles of the mains** (pipes of at least `main_min_mm`, default 160): an alignment (layer `WATER-ALIGNMENT`,
  turned off), a ground profile through the node elevations, and a profile view with the pipes, fittings and valves,
  stacked below the plan (`profile_views_at` moves them).

> **Note. Stand-in parts.** Civil 3D places pressure parts from its parts list. When the list has no part of the real
> material and size - the metric list of the standard template holds ductile iron 150-400 mm only - the nearest listed
> size that is not smaller than the WaterGEMS bore stands in (listed under `standIns`), and the description and label
> keep the real size. Depth allows for the stand-in, and `coverCheck` in the reply gives the cover of every built
> piece from its real outside diameter and elevations. Parts added to a list from the catalogue through
> the API could not be placed in Civil 3D 2027, so the import uses what the list already holds. To get exact parts, add
> PVC sizes to the parts list (or a catalogue that has them) in Civil 3D and run the import again on a fresh copy.

> **Note. Partial imports.** The network, labels/profiles and profile layout are separate transactions. Once the
> network is committed, every reply says `networkCommitted: true` and lists each stage (`stages`) with its error, so a
> failed finishing stage never hides the network already in the drawing.

> **Note.** No reducers or crosses are placed (the standard list has none). Nodes that would need one, and bends that
> match no catalogue angle, are listed under `fittingsSkipped`; the pipes still meet there.

### Plan and profile sheets from an office template

`pp_sheets_plan` (read) and `pp_sheets_create` (import tool) turn an office sheet drawing — for example
`PL23-RID-ICD-PRF.01-00.dwg`, layout `01` — into numbered layouts in the open drawing, one per sheet:

1. The template layout is copied with its page setup, title block xref (re-pathed to the template folder), notes,
   legend and its three viewports. The plan, profile and key-plan viewports are recognised by position and size
   (`viewports` overrides the choice with handles from `inspect_file`).
2. **Profiles.** `mode: views` uses the Civil 3D profile views in the drawing: they are grouped onto sheets in name
   order, and a view longer than `max_length` (default 500 m) is split over several sheets. `mode: rows` is for
   plain-CAD profiles stacked in rows (like `XR.ICD-PROFILES`): sheet *n* looks `row_spacing` (default 500 m) below sheet
   *n-1*, starting from the template's own profile window, and `sheets` gives the line names of each sheet.
3. **Plan.** The plan viewport is zoomed to the sheet's alignments plus 10 % at the smallest standard scale that fits
   (`plan: template` keeps the template's plan area).
4. **Texts.** The list of lines (layer `EN-LINE NAME`) is rewritten with the sheet's lines, the sheet label becomes
   `PLAN/PROFILE ( nn / NN )`, and the scale bar is resized to the plan scale.
5. **Key plan.** The highlight box moves to the sheet's plan area only when that area falls inside the key-plan
   viewport; otherwise it is left where it is and the reply says so.

```text
pp_sheets_plan   {"template": "C:\\...\\Ridge plan&Profile\\PL23-RID-ICD-PRF.01-00.dwg", "template_layout": "01"}
pp_sheets_create {... same ..., "layout_prefix": "PP-", "if_exists": "replace", "target": ..., "request_id": ...}
plot_pdf         {"layouts": ["PP-01", "PP-02", ...], "path": "C:\\...\\PP sheets.pdf"}
```

> **Note.** Any scale text drawn inside the scale-bar block (e.g. "SCALE 1:100") is resized with the bar but not
> rewritten; check it. Layouts are made in a first transaction and filled in a second, and the reply lists both stages.

## 5. What it cannot do

| Not possible | Why | What to do instead |
|---|---|---|
| Create parcels | The Civil 3D .NET API can only read them | Create them in Civil 3D and list them through MCP |
| Create grading objects (daylight slopes) | Not exposed by the API | Create a feature line and add it to a surface as a breakline: this gives the pad or channel surface, without automatic daylighting |
| Corridor targets (daylight to a surface, offset alignments), several regions | Not built yet | Set targets in Civil 3D after the corridor is created |
| Section-view sheets and bands, plan production | Not built yet | Views are placed in a grid with default styles |
| Create object styles, band styles, parts lists from scratch | Band contents are not exposed by the API; the others are not built yet | Use the styles of your template by name. Label styles can be created (`label_style_create`), and catalogue families can be added to a parts list |
| Civil 3D's own pipe / structure tables, interference check objects, intersections, superelevation calculation | Not exposed by the .NET API | `network_table` draws the schedule as an AutoCAD table (draw it again after changes); `network_interference` checks clearances directly; intersections and superelevation can be read, and critical-station slopes changed |
| Civil 3D 2026 or older than 2024 | Built for 2024, 2025 and 2027 only (2026 is not tested) | - |
| Change the size of an existing pressure part (`pressure_part_set` with `size`) in Civil 3D 2024 | The 2024 API has no swap-size method | Delete the part and add it again with the new size |
| One multi-sheet PDF of layouts on different papers (A1 + A0) | AutoCAD's plot engine puts only same-size sheets in one document | `plot_pdf` with `folder` (one PDF per layout), or make the papers alike with `page_setup_set` |
| Audit a drawing | Not in the .NET API (command only) | Run AUDIT in Civil 3D |
| Run AutoCAD commands or scripts | Left out on purpose (no arbitrary code execution) | - |

Also good to know:

- When you name no style, an edit uses the style called "Standard" if the drawing has one, otherwise the first style of
  that kind. Say the style name to get another one ("list the profile view styles" shows them).
- Civil 3D reports rejected breaklines (for example one crossing a surface point at another level) only in its
  Event Viewer.
- The plugin is not code-signed, so Civil 3D asks once whether to load it.

---

## 6. Safeguards and limits

- **Local only.** The plugin listens on `127.0.0.1` on a random port. Each Civil 3D session writes a new random token to
  `%LOCALAPPDATA%\C3dMcpBridge\bridge.json`; a request without it is refused.
- **Drawing target.** The bridge checks the session ID and active drawing before approval and under the document
  lock. Closing and reopening a DWG creates a new drawing ID.
- **Request status.** When a response is interrupted, call `get_edit_status` with the original `target` and
  `request_id`. The same edit ID and parameters are executed at most once per bridge session. A fresh edit or a
  changed payload needs a new ID. Status is held for the running session, with recent full results cached.
- **Audit log.** Every permission decision and every edit batch is written to `%LOCALAPPDATA%\C3dMcpBridge\audit.log`.
- **Nothing is forced.** Objects on locked layers or in xrefs make the batch fail with a message.
- **Overwrite protection.** Saving a copy, exporting or plotting onto an existing file needs `overwrite: true`.
- **Sanity limits.** 500 operations per batch; coordinates beyond 1e8 (plan) or 1e5 (elevation) are rejected as unit mistakes;
  requests up to 4 MB; lists are cut at their `limit` and say so (`truncated: true`).
- **Busy Civil 3D.** A request waits up to 30 seconds for Civil 3D (a running command or an open dialog blocks it) and is
  then cancelled before it starts. An edit that has started is always finished, never abandoned half-way.

> **Note.** The Yes / No dialog stops the tool from approving itself. It cannot stop a client session that also
> controls your screen from clicking "Yes", so only give such a session edit work you would approve yourself.

---

## 7. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| "Civil 3D is not reachable" | Civil 3D is not running, or the plugin did not load. Type `MCPSTATUS` in Civil 3D. If the command is unknown, the plugin is not loaded: check that `%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle` exists and restart Civil 3D; answer the load question with *Always Load*. |
| "Could not reach Civil 3D on port ..." | That Civil 3D was closed, or two are open. Type `MCPSTATUS` in the one you want. |
| "Unknown action" / "Unknown edit operation" | Check the server's startup mode and version. Restart the MCP client after updating. |
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
plugin/                     the Civil 3D plugin (C#; net48 for 2024, net8.0-windows for 2025, net10.0-windows for 2027)
  Compat.cs, AssemblyResolver.cs   .NET Framework 4.8 shims (Civil 3D 2024), compiled for net48 only
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
  catalog.py                names, descriptions and parameters of every action shown to MCP clients
  bridge_client.py          talks to the plugin
  qa.py                     QA checks computed from read actions
tests/                      smoke_test.py (no Civil 3D), live_read.py, live_edit.py, fake_bridge.py, *.bat launchers
tools/                      gen_docs.py (docs/ACTIONS.md), typecheck/ (compile without deploying), apitool/ (API signatures)
package/, build_package.bat the installer zip and the source zip
refs/YYYY/                  (git-ignored) Civil 3D reference DLLs for building a release not installed here
plugin/prebuilt/YYYY/       (git-ignored) last DLLs built per release, used by build_package.bat as a fallback
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
   - Throw `ArgumentException` for an invalid request. The MCP client receives the exception message.
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
| Python environment + offline test | `setup_server.bat` | `setup_result.txt` |
| Installer and source zips | `build_package.bat` | `dist\C3dMcp.zip`, `dist\C3dMcp-source.zip`, log `package_build.txt` (one `RELEASE 20xx:` line each) |

> **Note.** `live_edit.py` draws test objects. It refuses to run unless the active drawing is a new unsaved
> `DrawingN.dwg` or its name contains `mcp_test`. Run `MCPALLOW` first, or answer the dialog once.

### Building for 2024, 2025 and 2027

One source tree, three DLLs. Each is compiled against its own release's API and .NET runtime:

| Civil 3D | Series | Target | Bundle folder | Notes |
|---|---|---|---|---|
| 2024 | R24.3 | `net48` (.NET Framework 4.8) | `Contents\2024` | ships System.Text.Json and its dependencies; `Compat.cs` supplies missing .NET members |
| 2025 | R25.0 | `net8.0-windows` | `Contents\2025` | |
| 2027 | R26.0 | `net10.0-windows` | `Contents\2027` | |

A release is built when its DLLs are found: `AcadYYYYDir` in `plugin\local.props`, else a normal install in
`C:\Program Files\Autodesk\AutoCAD YYYY\`, else reference copies in `refs\YYYY\` (six DLLs; see
[refs/README.md](refs/README.md)). With the 2024 and 2025 DLLs copied into `refs\`, one PC builds all three and
`build_package.bat` reports `RESULT=OK`; otherwise it uses `plugin\prebuilt\YYYY\` for a missing release, or reports
`RESULT=OK_PARTIAL missing: ...`. Release-specific code uses `#if NETFRAMEWORK` (2024) or the symbols `C3D2024`,
`C3D2025`, `C3D2027`.

**Release routine.** Build and run the live tests on 2027 (`build.bat`, `tests\run_live_edit.bat`); build the package;
install `C3dMcp.zip` on the 2024 and 2025 PCs and run `MCPSTATUS` plus the live tests there (they need the source zip
and `setup_server.bat`).

The version is set in `plugin/BridgeInfo.cs` and `plugin/PackageContents.xml` (the test keeps them equal).
History: [CHANGELOG.md](CHANGELOG.md).

## Roadmap

Planned features with effort and priority: [ROADMAP.md](ROADMAP.md).

## Licence

Copyright (c) 2026 Mohamed Ahmed Hamed.

This project is **source-available** under the [PolyForm Noncommercial License 1.0.0](LICENSE.md):

- You may read, modify and use it for **non-commercial** purposes (personal projects, study, research, hobby work, and use by charities, schools and public bodies).
- **Commercial use, including use on paid projects and selling, needs written permission** from the copyright holder. Contact the author via GitHub to ask for a commercial licence.

Contributions are welcome through pull requests. By submitting one, you agree that it may be distributed under this licence.
