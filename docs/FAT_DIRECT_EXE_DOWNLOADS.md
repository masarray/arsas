# Direct EXE downloads for engineering FAT — opt-in public preview

## Why a PR build link still returns ZIP

The Windows Build ARSAS workflow already produces a real x64 single-file portable \`.exe\`. GitHub Actions stores and **downloads all workflow artifacts as ZIP**, even when only one EXE is uploaded. Changing upload-artifact names or extensions does not change this behavior. Do not label an Actions artifact URL as a direct EXE.

Published GitHub **Release assets**, by contrast, give direct binary URLs ending in \`.exe\`. ARSAS stable v1.6.41 already offers official direct portable/installer EXEs; these are **historical stable files**, not newer #659 FAT binaries.

## Proposed safe workflow

\`.github/workflows/fat-direct-exe.yml\` is a **manual-only, opt-in** preview publisher. It has no push, tag, PR, or workflow_run trigger. It can only be dispatched after the infrastructure file exists on the default branch and after the owner has explicitly approved **public non-production distribution**. Do not merge any unfinished P0 application/engine changes merely to install this infrastructure. An optional GitHub Environment named \`fat-preview-publication\` can require a human reviewer.

Required inputs:
- \`tested_sha\`: exact qualified 40-char candidate source commit.
- \`build_run_id\`: completed successful Build ARSAS run for that SHA; the workflow verifies its run identity, full set of exact-SHA workflow gates, downloaded PE signatures, and SHA-256.
- \`installer_run_id\`: optional separate successful installer validation run for the **same exact SHA**; use \`0\` when no installer exists.
- \`public_preview_ack\`: exactly \`PUBLISH PUBLIC NONPRODUCTION FAT EXE\`. This is a deliberate public-distribution authorization; without it the workflow aborts.

A successful explicitly authorized run publishes a **public GitHub prerelease** named \`fat-<SHA12>\`, never a stable release or "latest", with:
- \`ARSAS-FAT-<SHA12>-win-x64-portable.exe\`
- \`ARSAS-FAT-<SHA12>-win-x64-setup.exe\` **only if a fully qualified exact-SHA installer exists**
- \`SHA256SUMS.txt\`, source/build provenance receipts and a NOT-PRODUCTION warning.

Direct URLs then have the form:

\`\`\`text
https://github.com/masarray/arsas/releases/download/fat-<SHA12>/ARSAS-FAT-<SHA12>-win-x64-portable.exe
https://github.com/masarray/arsas/releases/download/fat-<SHA12>/ARSAS-FAT-<SHA12>-win-x64-setup.exe
\`\`\`

These are **URL templates, not existing download links** until the explicitly approved run finishes. Every SHA gets a fresh immutable tag; never overwrite existing preview binaries or stable v1.6.40/v1.6.41 assets. Do not change \`.release/windows.json\`, publish-verified production release metadata or \`main\` engineering acceptance through this workflow.

## Immediate #659 status

Current P0 Data Model #659 exact tested source:
\`1371c2990c40356acb1794bfa42c76e408421b47\`.
Windows 11/11 CI green, portable single-file artifact verified, but **Actions ZIP**:
https://github.com/masarray/arsas/actions/runs/39525163418/artifacts/12312721694

There is **no #659 direct EXE release asset yet**. Publishing a public prerelease (even explicitly marked FAT-only) must be approved by the repository owner before it runs. Installer for this exact candidate must pass its own Windows installer workflow before the optional setup.exe is made public. Do not substitute an older stable installer.

As a separate reminder, a different, already published production version currently has direct EXEs:
https://github.com/masarray/arsas/releases/download/v1.6.41/ARSAS-Windows-x64-Portable.exe
https://github.com/masarray/arsas/releases/download/v1.6.41/ARSAS-Windows-x64-Setup.exe
These assets do NOT contain the latest #659 Data Model patch.

## Security and regression

Run \`python scripts/test-fat-direct-exe.py\` before any preview publication. The tests reject SHA mismatch, incomplete/failing workflow run, disguised ZIP, and wrong/missing PE signature. The publication script also fails closed if fewer than ten exact-SHA workflow checks succeeded, if any are incomplete/failing, if the preview tag exists, or if any candidate executable failed independent local SHA sealing. No IED connection, read, write, release promotion or application runtime change is made.
