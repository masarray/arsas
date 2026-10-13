# ARSAS IED Browser — Live Value & MMS Inspection ARD

Status: engineering preview; requires physical validation, not a production release.

## Existing product authorities

- **ARIEC61850:** MMS association, typed report decoding, canonical SCL/discovery model, confirmed DataSet member identity, read-only MMS read, FileDirectory services.
- **ARSAS monitor runtime:** proven RCB activation, active per-IED report ingress, ordered process snapshots and SOE semantics.
- **Per-IED live value ledger:** cache indexed by canonical IEC object reference **and FC**. This ledger contains *observations*, not SCL design defaults, and is invalidated on disconnect.
- **Browser:** read-only virtualized projection of the ledger and immutable model. Navigating a category never calls a write, Operate or GI.

## Exact report-to-value pipeline

1. Receive a real report frame through the **existing** active MMS runtime. No duplicate report subscription.
2. Use only ARIEC semantic updates and the configured DataSet's model-verified member references to resolve FC. Reject ambiguous matches and unverified structured/container echoes.
3. Publish every report-contained scalar to the shared per-IED ledger **before** filtering to the user-selected Explorer point subset. Include quality evidence with the object's own IEC identity when present.
4. The existing 200ms WPF UI flush updates Browser visible rows **in place**, retaining selected rows, scroll, Back history and nested q/t expansions. Explorer and Live Monitor continue to use their established point snapshot projection. No synthetic SOE is created.
5. Missing observations remain **Not read**. If normalized quality is present, validity is derived; raw quality/time bit details not available from the current decode are **Not exposed**, never guessed.
6. Enabled RptEna does **not** guarantee that every DataSet member is included in each report. Initial image depends on a successful GI/integrity response, configured trigger options and actual inclusion bits. Status reflects received data, not merely an enabled control block.

## Read / Read All

- **Read:** model-proven ST/MX/CF/DC/SP/SG/SE/EX leaves belonging to the selected object, DataSet or configured RCB.
- **Read All:** every eligible model leaf, never CO/Operate/write control variables. Bounded per-leaf requests; user can Cancel.
- **When not monitoring:** use the existing serial per-device MMS runtime, with verified fast connect if the IED is offline but the model/endpoint permit it.
- **When report monitoring is active:** a separate, short-lived *read-only* MMS association is used to avoid affecting reporting subscription state. An IED that refuses a second association must show an explicit error; no fallback writes.
- A Read sample can update exact matching signal/point models when the monitor is stopped. When monitoring is active, Read does not replace authoritative report point state or create an SOE.
- Progress is rendered on the existing IED Card independently of the report lifecycle busy state.

## RCB usage badges

- BRCB and URCB have distinct, compact badge colors.
- **ARSAS active:** proven by a successfully started local report plan, held only for the current association.
- **Another client in use:** demonstrated by a separate explicit read-only availability check. Never infer occupancy from SCL declarations or matching RptID.
- Unknown ownership stays unknown. RCB writes, reservation and GI cannot be triggered by Browser selection.

## Files

- Browse Files and selection of the Files category reuse the **FaultRecordTransferClient** FileDirectory/COMTRADE service. No duplicate transport stack.
- The currently supported remote record-file subset shows real filename, path, size and last-modified evidence. This does not imply that every vendor-specific file is supported.
- Browse is finite and cancellable on association or selection change; data are not persisted as live information after disconnect.

## Acceptance gates

1. ZIV AVR static DataSet GI/integrity: compare every *actually included* scalar (FC and exact object reference) with Browser on same association; test report-only with no cyclic MMS process reads.
2. BCUGE multi-RCB: validate own RCB and other-client status badges against real MMS RptEna/Resv/Owner observations; verify URCB/BRCB color distinction.
3. Read and Read All: test idle, disconnected verified-model fast connect, active report (independent association), IED refusing a second connection, cancel and partial results.
4. q/t: validity and timestamps must agree across Browser/Live Monitor; never infer unavailable raw bits.
5. Files: compare file catalog to the independent Fault Records window, including filenames, sizes and modified times.
6. CI: exact head WPF build, pinned engine suite, ARSAS tests, native bridge and portable EXE smoke test must all pass.
7. Release remains gated by screenshot/physical acceptance; do not replace the public stable release on a draft branch.
