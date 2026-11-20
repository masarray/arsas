# ARIEC61850 Engine Compatibility

ARSAS v1.6.40 is an application-only repository. It compiles against the separately maintained ARIEC61850 source projects and uses engine-owned contracts for MMS, reporting, GOOSE, Sampled Values, file services, control, SCL workspace services, discovery, and diagnostics.

## Immutable integration baseline

The reviewed engine revision used by CI and packaging is stored in:

```text
engines/ARIEC61850.lock.json
```

Current v1.6.40 stable baseline:

```text
ARSAS tag: v1.6.40
ARSAS release source: 1b26dc1235b3bac0300bf84c1b5e8fc590934bc5
repository: masarray/ARIEC61850
ref: main
commit: 648124097621046f5f127ceb1cf853fea54db730
source PR: #135
```

The [installed-release field acceptance](docs/V1-6-40_INSTALLED_RELEASE_FIELD_ACCEPTANCE.md) confirms this exact application/engine pair and successful physical Smart Discovery with 58/58 static report-backed runtime points and zero cyclic MMS process polling. The historical P0-5g promotion switch does not select the current production route: the tracked Smart Discovery entrypoint is verified from source before building, without workflow-time source mutation.

Do not change the pinned engine or compile a historical discovery path merely to fix documentation or post-release issues. Subsequent mainline commits do not retroactively change the published v1.6.40 binary.

CI fetches the exact 40-character commit and checks it out detached. A temporary feature branch is not part of the reproducible build identity.

## Required engine areas

The build verifies the presence of application-consumed contracts for:

- Smart Control and `CommandTermination`;
- SCL workspace comparison and schema-aware export;
- typed DataSet binding and stable member order;
- GOOSE parsing and process-bus supervision;
- Sampled Values parsing, generic payload inspection, timebase resolution, sample-counter tracking, and Npcap capture;
- file-service, discovery, reporting, and diagnostic services referenced by the application project.

If a required contract is missing, CI and local builds fail explicitly rather than silently degrading the operator workflow.

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

Another reviewed engine checkout may be selected with the existing MSBuild properties or environment variables, but release evidence must record the exact commit and must not imply compatibility with an unreviewed moving branch.

## Source and package boundary

ARSAS does not overwrite or vendor the ARIEC61850 source repository. A published Windows package may contain compiled engine assemblies as part of the combined GPL community application, while reusable protocol implementation remains owned and tested in ARIEC61850.

Control operations intentionally have no generic MMS-write fallback. The native engine must provide a usable control descriptor and sequence contract before ARSAS enables command dispatch.

## Claim boundary

A successful build and regression suite proves compatibility with the pinned software revision. It does not establish formal IEC 61850 conformance, calibrated measurement accuracy, universal device interoperability, switching authority, cybersecurity approval, or functional-safety certification.
