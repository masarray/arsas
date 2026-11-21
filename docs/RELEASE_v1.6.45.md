# ARSAS v1.6.45 — Engineering Grid Clipboard UX

## Release scope
Public Windows x64 IEC 61850 engineering tester. This maintenance release focuses on engineering observation, repeatable selection, and copying exact displayed data; it does **not** modify the IEC 61850 runtime, protocol authority, engine pins, or control admission.

## Changes since v1.6.44
- **Event Log:** Extended + FullRow selection with drag and Shift+click; right-click Copy/selected rows and Ctrl+C; preserve the selection viewport while the event inspector drawer is closed or right-click context is active.
- **IED Browser:** Copy value, field/object name, name and value, and visible properties directly from Report ID, RCB Name, Control Block reference and other visible properties. Use identity of the clicked current row; stale rows after another IED/model refresh cannot be copied. Compact RCB object names are distinct from their status text.
- **Existing grid experience:** Device Explorer/Command Grid and Live Value Viewer clipboard behavior from prior updates is retained. Shared clipboard serializer remains bounded and read-only; specialized menus and all engineering Write/Control affordances are preserved.

## Validation and release authority
- Original user acceptance was performed interactively on workstation for both #783 and #784; GitHub's exact PR-head Windows application regression builds and packaging passed.
- The independent official release workflow must verify the **exact merged main commit's** canonical Windows package, installer, install/uninstall smoke, checksum files, SPDX SBOM and provenance before it can publish new stable assets. Do not claim publication on the basis of this document alone.
- ARIEC61850 and ArdIrec locks remain the same as v1.6.44. Historical R7/R10 physical acceptance guards may still report pre-existing code-trial provenance limits; this UI maintenance is not a waiver or requalification.
- This engineering tester is not IEC 61850 conformance certification, a SCADA system, or evidence that every vendor's reporting is lossless. No new physical multi-vendor qualification is claimed.
- Public Windows binaries remain commercially Authenticode-unsigned; SmartScreen warnings are possible. Verify the published SHA-256 hashes.

Previous stable: [v1.6.44](https://github.com/masarray/arsas/releases/tag/v1.6.44).
