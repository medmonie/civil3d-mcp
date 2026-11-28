# Civil 3D .NET API vs MCP 0.23 - gap analysis

28 Nov 2026. Scope: `CIVIL 3D MCP\civil3d-mcp` at 0.23.0 (the ChatGPT folder is an older copy of the same tree, not analysed separately).
The packages below are on the roadmap as #69 - #80 (ROADMAP.md, "API gap packages").

## Method

- **API surface**: public types and methods read from the metadata of `refs\2027\AeccDbMgd.dll` and `AeccPressurePipesMgd.dll` (1,634 public types; 995 outside `Styles` / `Settings`). Diffed against `refs\2024` for version gating.
- **MCP surface**: every `.cs` file under `plugin\` (grep for type and method names), `docs\ACTIONS.md` (75 reads, 133 edit operations), `server\server.py`.
- **Limit**: name-based. A type reached only through `var` can look unused, so every gap below was confirmed by the absence of its *method* names. The AutoCAD side (acdbmgd) was spot-checked, not enumerated. Data shortcut, survey and COM assemblies are not in `refs\`, so "not in API" below means "not in the referenced assemblies".

**Coverage: about 190 of 995 Civil types are used.** Most of the unused remainder are enums, collections, label-group variants and style plumbing; the gaps that matter are listed below.

## Confirmed API limits (roadmap is right)

| Item | Evidence |
|---|---|
| Grading | `Grading` type has no public members; no `GradingGroup` |
| Parcel creation | `Site.Create` exists; no parcel or parcel-segment create |
| Civil tables (network, point, parcel, alignment) | IDs readable via `CivilDocument.Get*TableIds`; no create |
| Mass haul | `MassHaulLine` / `MassHaulView` have no members; only `SampleLineGroup.GetMassHaulViewIds` |
| Plan Production create | `ViewFrameGroup`, `ViewFrame`, `MatchLine`, `Sheet` readable and label-settable only |
| Data shortcuts | only `DataShortcutKey` / `DataShortcutEntityType` enums in AeccDbMgd |
| LandXML export, survey database | import enums / settings only; survey is COM |

**Correction to ROADMAP #13 (data shortcuts, M):** not doable from the referenced .NET assemblies; needs the command fallback below, or a check for a separate data-shortcut assembly in the Civil 3D install folder.

## Gaps: in the API, not in the MCP

### A. Gravity networks (P1 - post-import editing) - roadmap #69
- `Network.BreakPipe` (insert a structure in an existing pipe), `Network.MoveParts` (between networks)
- `Pipe.Disconnect`, `Structure.Disconnect`, `Pipe.AdjustEndpoint`
- `Pipe.SetSlopeHoldStart` / `SetSlopeHoldEnd` (regrade holding one end)
- `Structure.ResizeByPipeDepths`, `ResizeJunctionStructure`
- `Pipe.FlowDirectionMethod` (reverse / by slope)
- Read: `Network.FindShortestNetworkPath` (trace upstream / downstream between two parts), `Structure.GetConnectedCatchmentIds`
- Optional fact flags: `Pipe.IsMinCoverViolated` / `IsMaxCoverViolated` (Civil 3D's own rule result, not an MCP design check)

### B. Pressure networks (P1 - WaterGEMS follow-ups) - roadmap #73
- `PressurePipeRun.Break`, `RemoveBendByPI`, `RemoveVerticalBendByPVI`
- `FollowProfile`, `ResetReferenceProfile`, `SetVerticalOffsetType`

### C. Labels (P1/P2 - sheet quality) - roadmap #74
Creatable in the API, unused: `SpanningPipeLabel`, `SpanningPipeProfileLabel`, `CrossingPipeProfileLabel` (2027 only), `CrossingPressurePipeProfileLabel`, `PipeSectionLabel`, `StructureSectionLabel`, `PressurePipe/Fitting/AppurtenanceSectionLabel`, `ProfileViewDepthLabel`, `SectionViewDepthLabel`, `SectionViewOffsetElevationLabel`, alignment label groups (station, minor station, PI, indexed PI, geometry point, design speed, superelevation, station equation), profile label groups, sample-line and section label groups, `ViewFrameLabelGroup` / `MatchLineLabelGroup`, `GeneralSegmentLabel`, `ParcelSegmentLabel`, `CatchmentLabel`, `FlowSegmentLabel`.

### D. Surface editing (P2) - roadmap #75
- TIN edits: `AddLine`, `DeleteLine(s)`, `SwapEdge`, `AddVertex`, `DeleteVertex/Vertices`, `MoveVertex`, `SetVertexElevation`, `RaiseSurface`, `RaiseVertices`
- Cleanup: `SimplifySurface`, `SmoothSurfaceByNNI`, `SmoothSurfaceByKriging`, `MinimizeFlatAreas`
- Masks: `SurfaceMaskCollection.Add` / `SurfaceMaskCreationData`
- Reads: `GetTriangles`, `FindTriangleAtXY`, `GetVerticesInsidePolylines`, `CreateFromIMX`

### E. Alignments and profiles (P2) - roadmap #77
- `Alignment.ReferencePointStation`, `StationIndexIncrement` (starting chainage), `Reverse`, `CopyToSite`, `GetPolyline`
- Reads: `GetStationSet` (stations at intervals / geometry points), `DistanceToAlignment`, `GetCrossSlopeAtStation`, `GetStationStringWithEquations`
- `Profile.UpdateMode` (dynamic / static), `Profile.GradeAt`
- `ProfileView.FindStationAndElevationAtXY` (inverse of the one used), split profile views (`SplitProfileView`, `SplitHeight`, `SplitDatumRounding`), `StackedProfileViewsCreationOptions`
- `SuperelevationView` create

### F. Corridors and quantities (P2) - roadmap #77
- Baseline transitions: `ExportTransitions`, `ImportTransitions`, `SetTransitions`; `Baseline.SetFeatureLine`
- Regions: `Match`, `ClearAdditionalStations`, `GetOverriddenStations`
- `Corridor.GetLinkCodes` / `GetShapeCodes`, `MaximumTriangleSideLength`, `RegionLockMode`; corridor surface masks
- QTO: `QTOUtility` pay items (`OpenPayItemFile`, `AddPayItemId`), `GenerateXMLReport`; `SampleLineGroup.ReportQuantities`
- `SectionViewGroup.UpdateLayout`, `ImportLabelSetForSectionsInGroup`

### G. Data on objects (P1 - small and enabling) - roadmap #70, #78
- **User-defined property values cannot be written**: `udp_create` and `get_user_property_values` exist, but `SetUDPValue` is never called (COGO points, parcels).
- Parcel edits: `Number`, `Address`, `TaxId`, style, area-label settings.
- Point groups: `DeletePoints`, `LockPoints`, `UnlockPoints`, `GetPendingChanges`.
- Reads of existing Plan Production (`GetViewFrameGroupIds`, view frames, match lines) and of existing Civil tables (`GetPointTableIds`, etc.).

### H. AutoCAD side (spot check) - roadmap #79
Not used: `Field` (ROADMAP #38), `Group`, `XData` / `Xrecord` / extension dictionaries, `Wipeout`, `Spline`, `Ellipse`, `SelectionFilter` / `TypedValue`, `DataLink` (Excel-linked tables), `LayerFilter`, `Publisher` / `DsdData` (ROADMAP #46).

## Architecture gaps

1. **No command fallback** (roadmap #76). Nothing calls `Editor.Command` / `CommandAsync` / COM. An allowlisted command runner (fixed command set, UNDO begin/end mark, verify-after, audited) would unblock grading-by-command, LandXML export, data shortcuts, AUDIT (#40), Publish (#46) and Civil network tables. It cannot join the single-transaction rollback, so it needs its own receipt type.
2. **Narrow generic setter** (roadmap #71). `object_set` only changes name, description, style and layer. A reflection-based `property_set` limited to an allowlist of Civil setters (pipe flow direction, sump depth, reference station, profile update mode, corridor triangle length...) would close many single-property gaps in one operation; `describe_api` already reflects.
3. **No object provenance** (roadmap #72). Imported objects carry no source ID. Writing XData / Xrecords (SewerGEMS / WaterGEMS element ID, import run) makes ROADMAP #6 (band table refresh) and #8 (model vs drawing) reliable instead of name-matched.
4. **MCP protocol** (roadmap #80). Uses tools, annotations, progress heartbeats and structured errors. Not used: resources (drawing state, ACTIONS.md signatures), prompts, list-changed notifications. Moving signatures to resources / `help` addresses the 43.7 KB tool-description open item in AUDIT_0.23.

## Version gating (2027 vs 2024)

38 types and several members exist in 2027 but not 2024, including: catchment runoff and infiltration models (`CatchmentRunoffSCS/SWMM/Rational/...`, `TR55FlowSegment`), `CrossingPipeProfileLabel`, pressure-part overrides in profile views (`PressurePipeOverride` etc.), `Alignment.TrackDistanceToAlignment` / `CopyToSameSite`, `FeatureLine.Reverse` / `ExtendWithFixedLine`, `Pipe.ManningCoefficient` and loss coefficients. Anything built on these needs a `Compat` gate; 2025 was not diffed.

## Suggested next order

| # | Package | Roadmap | Effort | Unblocks |
|---|---|---|---|---|
| 1 | Gravity network edit pack (A) + `network_trace` read | #69 | M | Edits after import without re-importing |
| 2 | `udp_set` + parcel edits (G) | #70 | S | Writing data back to objects |
| 3 | Allowlisted `property_set` | #71 | S-M | Many single-setter gaps at once |
| 4 | XData provenance on imports | #72 | S-M | ROADMAP #6, #8 |
| 5 | Pressure run edit pack (B) | #73 | M | WaterGEMS reducers, crosses, hydrant branches |
| 6 | Labels pack (C) | #74 | M | Plan / profile / section sheet quality |
| 7 | TIN edit pack (D) | #75 | M | Survey surface cleanup |
| 8 | Command fallback runner | #76 | L | LandXML export, data shortcuts, audit, publish, Civil tables |
