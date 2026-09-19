# ARIEC61850 Engine Compatibility

ARSAS **1.6.38** is the Windows application and workflow layer. It compiles
against the separately maintained ARIEC61850 source projects for MMS, reporting,
GOOSE, Sampled Values, file services, control, SCL workspace services,
discovery, diagnostics, and reusable protocol contracts.

## Immutable integration baseline

The exact engine revision used by CI and stable packaging is stored in:

```text
engines/ARIEC61850.lock.json
```

Current baseline:

```text
repository: masarray/ARIEC61850
ref: main
merged commit: 648124097621046f5f127ceb1cf853fea54db730
source PR: #135
physical-tested commit: 9935d6902d786cc69b299260fe36b835944d5e81
merged/tested source-tree SHA: 1cf7e08f333f24994625e8fe8416dbd0a16195b1
```

The physical-tested commit and merged engine authority resolve to the identical
source tree. ARSAS release packaging therefore consumes the reviewed merged
engine tree without substituting a different implementation.

## ARSAS 1.6.38 acceptance

The R10 acceptance line requires the engine/application pair to preserve:

- one bounded structural-discovery association;
- exact LD/LN/DO/SDO/DA/FC identity;
- exact-case IEC 61850 value identity;
- DO-scoped CF hydration for multi-DO structures;
- canonical Edition 2 / Edition 1 SCL round trip;
- 32/32 trusted-SCL MMS domain reconciliation;
- zero failed planned initial Reads;
- `projectionErrors=0`;
- `cacheLoss=0`;
- 58/58 report-backed runtime points;
- zero final unresolved runtime points;
- actual InformationReport traffic;
- zero cyclic MMS process polling in the accepted report-backed path.

The detailed physical contract is documented in
[Interoperability convergence](docs/INTEROPERABILITY_CONVERGENCE.md).

## Required engine areas

Build and release validation requires the application-consumed contracts for:

- Smart Control and `CommandTermination`;
- SCL workspace comparison and schema-aware export;
- typed DataSet binding and stable member order;
- report-control discovery and logical/runtime identity projection;
- GOOSE parsing and process-bus supervision;
- Sampled Values parsing, generic payload inspection, timebase resolution,
  sample-counter tracking, and Npcap capture;
- MMS file-service, discovery, reporting, and diagnostic services.

If a required contract is missing, CI and local builds fail explicitly instead
of silently degrading the engineering workflow.

## Recommended sibling layout

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\
│     ├─ AR.Iec61850\AR.Iec61850.csproj
│     └─ AR.Iec61850.Transports.Npcap\AR.Iec61850.Transports.Npcap.csproj
└─ arsas\
   ├─ ArIED61850Tester.csproj
   └─ tests\ARSAS.Tests\ARSAS.Tests.csproj
```

Build and test with the default sibling references:

```powershell
dotnet restore .\ArIED61850Tester.sln
dotnet build .\ArIED61850Tester.sln -c Release --no-restore
dotnet test .\tests\ARSAS.Tests\ARSAS.Tests.csproj -c Release --no-build --no-restore
```

Another reviewed engine checkout may be selected with the existing MSBuild
properties or environment variables, but release evidence must record the exact
commit and must not imply compatibility with an unreviewed moving branch.

## Source and package boundary

ARSAS does not copy the ARIEC61850 source tree into this repository. Stable
Windows packages contain the compiled engine components required by the
combined GPL community application, while reusable protocol implementation
continues to be maintained and tested in ARIEC61850.

Control operations intentionally have no generic MMS-write fallback. The engine
must provide a usable control descriptor and sequence contract before ARSAS
enables command dispatch.

## Claim boundary

A successful build and regression suite proves compatibility with the pinned
software revision. It does not establish formal IEC 61850 conformance,
calibrated measurement accuracy, universal device interoperability, switching
authority, cybersecurity approval, or functional-safety certification.
