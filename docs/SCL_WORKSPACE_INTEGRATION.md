# ArIED SCL Workspace Integration

ArIED delegates SCL parsing, endpoint resolution, type-template projection, and expected-versus-live comparison to the reusable ARIEC61850 engine.

## Open SCL

`MainWindow.OpenScl_Click` calls `SclWorkspaceService.OpenAsync`. Opening an ICD, CID, IID, SCD, SSD, or XML SCL file is offline and sends no MMS traffic.

For each IED and AccessPoint returned by the engine, ArIED creates an independent workspace containing:

- IED and AccessPoint identity;
- direct MMS endpoint when present;
- LD/LN/DO/DA model projected from `DataTypeTemplates`;
- static DataSet and ReportControl bindings;
- GOOSE and Sampled Values engineering metadata;
- source file path and SHA-256 provenance;
- typed SCL findings.

An ICD without `Communication` remains browseable. Pressing Play opens the existing endpoint wizard so an MMS address can be bound locally.

## Connection behavior

Play uses the existing cached-model connection path when an SCL design model is available:

```text
SCL workspace
→ offline signal projection
→ TCP/ACSE/MMS association
→ selected-point verification and acquisition
```

It does not repeat full GetNameList discovery. Dynamic DataSet writes default to disabled for SCL workspaces.

Re-scan intentionally performs full live discovery. ArIED converts the resulting runtime signal inventory into a bounded `LiveIedModelDiscoveryDocument` and sends both expected and observed projections to the engine-owned `SclLiveModelComparer`. Compatible models are marked verified. Missing visible attributes, FC/type differences, DataSet/RCB differences, or identity mismatch are surfaced as configuration drift in Diagnostics.

Control objects remain unavailable for operation until ArIED performs the existing live `ctlModel`, Oper/SBOw/Cancel, and exact MMS type inspection.

## Project persistence

Project schema version 3 stores:

- SCL source path;
- SHA-256 source hash;
- IED name;
- AccessPoint name;
- cached signal projection and user selections.

When reopening a project, ArIED reloads the engine workspace only when the source hash still matches. A missing or changed source file never silently replaces the cached model.

## Dependency gate

Until ARIEC61850 PR #25 is merged, the ArIED CI workflow pins `ARIEC61850_REF` to `agent/scl-workspace-api`. After merge, change the value to `main` in `.github/workflows/build.yml`.
