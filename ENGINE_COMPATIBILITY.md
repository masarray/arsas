# ARIEC61850 Engine Compatibility

ArIED 61850 v1.6.6 is an application-only repository. It compiles against the separately maintained ARIEC61850 source project and uses native engine contracts for control, SCL workspace services, discovery, reporting, and diagnostics.

## Required engine areas

The build currently verifies the presence of:

```text
src/AR.Iec61850/Control/Iec61850ControlService.cs
src/AR.Iec61850/Control/Iec61850ControlModels.cs
src/AR.Iec61850/Scl/Workspace/SclWorkspaceService.cs
src/AR.Iec61850/Scl/Workspace/SclWorkspaceModels.cs
```

The compatibility gate also checks for key contracts used by the application, including:

- `IIec61850ControlService`;
- `CommandTerminationReceived` in the control result contract;
- design-to-live comparison support in the SCL workspace service.

If a required contract is missing, CI and local builds should fail explicitly rather than silently degrading the operator workflow.

## Recommended sibling layout

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\AR.Iec61850\AR.Iec61850.csproj
└─ ArIED61850Tester\
   └─ ArIED61850Tester.csproj
```

Build with the default sibling reference:

```powershell
dotnet build .\ArIED61850Tester.csproj -c Release
```

Or select another engine checkout:

```powershell
dotnet build .\ArIED61850Tester.csproj -c Release `
  -p:ArIec61850Project="D:\Engineering\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
```

The environment variable form is also supported:

```powershell
$env:ARIEC61850_PROJECT = "D:\Engineering\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
```

## Source and package boundary

ArIED does not overwrite the engine repository. A published Windows package may contain compiled engine assemblies as part of the combined GPL community application, but this application repository does not vendor or duplicate ARIEC61850 source.

Control operations intentionally have no generic MMS-write fallback. The native engine must provide a usable control descriptor and sequence contract before ArIED enables command dispatch.

## CI integration reference

The exact engine branch or revision used by CI is defined in `.github/workflows/build.yml`. Release preparation should pin or document a reviewed engine revision and update this file when the supported integration baseline changes.
