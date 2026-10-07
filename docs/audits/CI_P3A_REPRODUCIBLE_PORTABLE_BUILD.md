# CI-P3A — Reproducible Windows portable build identity

Issue #447.

## Purpose

P2F proved that matching ARSAS and ARIEC61850 revisions could still produce
different portable single-file executables in independent workflows. Therefore
binary artifact reuse remains prohibited until reproducibility is proven.

## Root-cause hypothesis and controlled change

The Windows package embeds a pinned ArdIrec native bridge. That bridge is built
independently with MSVC/CMake in each workflow. A PE linker timestamp or other
non-reproducible native metadata can change the embedded bridge and, because the
single-file payload is compressed, propagate into a large executable diff.

P3A does not treat that hypothesis as accepted evidence. It makes the native
shared-library linker request reproducible output with /Brepro and makes the
.NET publish determinism intent explicit with Deterministic=true and
ContinuousIntegrationBuild=true. Existing ArdIrec native regression tests remain.

## Build identity

Each single-file publish writes a JSON identity next to the executable containing:

- ARSAS source commit when the source is a Git checkout;
- ARIEC61850 engine commit when the project is a Git checkout;
- pinned ArdIrec lock commit;
- ArdIrec bridge SHA-256 and byte size;
- portable executable SHA-256 and byte size;
- version/runtime and deterministic-build flags.

Both Build ARSAS and Smart Discovery Field Capture upload that identity.

## Acceptance boundary

P3A itself does not authorize installer/release artifact reuse. After CI, the two
independent builders must report identical source, engine and ArdIrec inputs and
identical bridge plus portable SHA-256. Only that measured result can unlock P3B.

Installer/release/physical authority and runtime behavior are unchanged.

## Efficiency

No new heavy workflow is introduced. Existing canonical and Field Capture lanes
serve as the independent builders, so reproducibility evidence is obtained from
work already required by the repository.
