# ARSAS v1.6.46 — Smart Updater first-download reliability

## Release scope
Public Windows x64 IEC 61850 engineering tester maintenance release built from the reviewed `main` after [PR #789](https://github.com/masarray/arsas/pull/789). This release fixes the already-existing Smart Updater's first installer download; it does not add new IEC 61850 device behavior.

## What changed
- **Correct Windows file lifecycle:** fully flush and dispose the exclusive `.part` installer writer before opening the partial download for independent SHA-256/size verification and final rename to the verified installer cache. This prevents Windows sharing violations on first download while avoiding any publication of unverified bytes.
- **Verified cache reuse and failure containment:** a subsequent update attempt re-verifies the cached package without redundant HTTP fetch; a mismatched SHA-256 rejects the payload and removes the incomplete file.
- **Regression tests:** exact update transport tested with offline simulated HTTPS responses for 1.1 MB installer payload, cached reuse, mismatched digest failure and temporary-file cleanup.
- **Existing smart updater already integrated:** earlier draft PR #771 was reconciled and closed as superseded, not re-merged. The v1.6.45 source already has delayed background review, verified prefetch, a non-modal prompt, explicit operator permission, stable manifest provenance checks, and isolated stable/RC caches; these remain unchanged.

## Publication and website authority
- The Windows release workflow must validate the **exact merged `main` commit** with canonical Windows Build, installed/uninstalled installer runtime smoke tests, portable EXE checks, SHA-256 checksums, SPDX SBOM and provenance *before* making v1.6.46 public.
- The independent release sync must update `landing/latest.json` and bilingual `landing/release-notes.json` **from the already published stable GitHub release**, then deploy Pages. Do not edit the public latest version ahead of published assets.
- Installer/portable download URLs deliberately use `releases/latest/download` so existing site and README buttons resolve to the newest verified stable assets.

## Scope and limitations
- This release does **not** change the ARIEC61850 engine, IEC 61850 MMS control/monitoring path, BRCB reporting, Buffer Event labeling, reconnect behavior, command admission, RCB ownership, or PurgeBuf safeguards.
- The confirmed per-operator SOE Buffer Event behavior remains in the previous stable build; this release does not claim certified, lossless event recovery or universal vendor qualification.
- The Windows binaries remain commercially Authenticode-unsigned. SmartScreen may show a warning; verify published SHA-256 checksums before installation.

Previous stable release: [v1.6.45](https://github.com/masarray/arsas/releases/tag/v1.6.45).
