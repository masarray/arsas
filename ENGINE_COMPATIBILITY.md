# ARIEC61850 engine compatibility

ArIED 61850 v1.6.5 uses the native Smart Control API from the connected ARIEC61850 source repository.

Required engine source includes:

```text
src/AR.Iec61850/Control/Iec61850ControlService.cs
src/AR.Iec61850/Control/Iec61850ControlObjectSession.cs
src/AR.Iec61850/Control/Iec61850ControlModels.cs
src/AR.Iec61850/Control/Iec61850ControlValueBinder.cs
src/AR.Iec61850/Control/Iec61850CommandTerminationDecoder.cs
```

The integration was audited against the uploaded ARIEC61850 revision `41d003ae02b1003d16cd5a8baf5f7a95be4434fa`.

ArIED does not ship or overwrite the user's ARIEC61850 repository. Build it against the existing engine checkout:

```powershell
dotnet build .\ArIED61850Tester.csproj -c Release `
  -p:ArIec61850Project="D:\Git\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
```

The default sibling layout remains supported:

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\AR.Iec61850\AR.Iec61850.csproj
└─ ArIED61850\
   └─ ArIED61850Tester.csproj
```

ArIED v1.6.5 intentionally has no legacy generic-MMS control fallback. If the Smart Control API is absent or its contract is older, compilation should fail rather than silently enabling unsafe command writes.
