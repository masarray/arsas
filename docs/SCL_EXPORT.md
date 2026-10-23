# Per-card interoperable SCL export

Each ARSAS IED card exposes a **Save SCL** action after the application has a complete typed model or a trusted opened SCL design source.

## Supported sources

- **Opened SCL design model** — Edition 2 uses the engine-owned generic interoperability converter against the original source file and the IED represented by that card. Edition 1 is rebuilt from the typed `SclWorkspace.DesignModel` using the schema-aware exporter.
- **Live MMS discovery** — Edition 2 and Edition 1 are generated from the last successful full IP-discovery model. A saved signal cache alone is not treated as complete engineering evidence; use **Re-scan** to capture a full model first.

## Edition choices

- **Edition 2 (Schema V3.1)** → `.iid`
- **Edition 1 (Schema V1.6)** → `.icd`

The chosen profile controls root schema metadata, supported ReportControl fields, Services declarations, and default file extension.

## ReportControl identity in ARSAS 1.6.37

ARSAS separates the declarative engineering model from concrete runtime RCB instances.

A source SCL file may describe one logical `ReportControl` together with `RptEnabled@max`. A connected IED can expose concrete client/runtime instances such as `Buffer01`, `Buffer02`, `Unbuffer01`, or `Unbuffer02`. Those online instances are valid live evidence, but they are not authority to duplicate or rename the logical source `ReportControl` in a source-backed IID/SCL export.

The v1.6.37 contract is therefore:

- live RCB selection presents the concrete runtime instances actually exposed by the connected IED;
- duplicate logical placeholder rows are not added beside those live instances;
- source-backed export preserves the canonical source `ReportControl` identity and its `RptEnabled` indexing metadata;
- declarative `RptEnabled@max` metadata never invents runtime RCB instance names;
- live-model-only export remains concrete and uses the discovered live model because no source SCL identity exists to preserve.

This is intentionally the same separation an engineer expects between online client slots and the saved engineering model.

## Companion evidence

The ARIEC61850 export services write the SCL file together with a JSON report and a Markdown summary. The schema-aware discovery exporter also records excluded-attribute evidence where applicable. Conversion findings and export warnings remain visible in ARSAS Diagnostics and in the companion report.

## Scope

The output reflects the typed model available from the opened file or the latest successful MMS discovery. Engineering information that cannot be obtained from the selected authority is not invented. Export success is interoperability evidence, not a claim of formal IEC 61850 conformance, device acceptance, or operational approval.
