# Direct Windows EXE downloads for engineering FAT

## The fast path

**GitHub Actions artifacts are ZIP containers, by design**, even when one
`.exe` was uploaded. Renaming an Actions artifact to `.exe` would mislead
the user and does not change the bytes.

The stable GitHub Releases assets already download directly:
- https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.exe
- https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe

Those URLs are **stable release builds**, not necessarily the latest open PR.

For *development/FAT* binaries, the existing
`.github/workflows/installer-windows.yml` now includes an independently
gated manual `fat-direct-preview` job. We deliberately did **not** add
a new workflow file or another checkout; the repository's
`CI P0 Workflow Integrity` restricts CI workflow/checkout counts.

## Publish a tested PR binary as one-click .exe (maintainers)

Once this infrastructure change has passed CI and been approved on `main`:

1. Open **Actions → Validate ARSAS Windows installer → Run workflow**.
2. Choose branch **main** and enable **fat_preview**.
3. Paste **tested_sha** (full 40-character, already all-green PR head)
   and the successful **Build ARSAS** workflow **build_run_id** for that exact SHA.
4. Leave **installer_run_id=0** for portable EXE only, or supply the completed
   exact-SHA installer workflow run ID for both direct EXEs.
5. For a deliberately **PUBLIC**, non-production engineering preview, type
   **PUBLISH PUBLIC NONPRODUCTION FAT EXE** in **public_preview_ack**.
   The dedicated `fat-preview-publication` environment may require reviewer
   approval. This is never triggered by PR/push/tag automatically.

After the public preview is approved and published, the direct download is:

```text
https://github.com/masarray/arsas/releases/download/fat-<SHA12>/ARSAS-FAT-<SHA12>-win-x64-portable.exe
```

If a separate exact-SHA installer succeeded and its ID was entered:

```text
https://github.com/masarray/arsas/releases/download/fat-<SHA12>/ARSAS-FAT-<SHA12>-win-x64-setup.exe
```

These are **templates**, not valid URLs until the preview release is actually
published. Download is one click and does not require unzipping.

## Proof and immutability

The publisher:
- Runs offline positive/negative verifier tests from the exact approved
  `main` workflow commit, without executing candidate-branch scripts.
- Requires all current exact-source CI workflows completed successfully,
  with at least 10 results, and a successful exact-SHA Build ARSAS run.
- Downloads the canonical Actions artifact and validates the actual
  Windows PE bytes, build run identity and SHA-256; an optional installer
  is independently validated against the exact source SHA.
- Publishes a new immutable `fat-<SHA12>` **prerelease** with
  `SHA256SUMS.txt`, provenance files and `NOT-PRODUCTION.txt`.
  It refuses to overwrite an existing preview tag.
- Does **not** alter stable releases, `latest`, application version,
  `.release/windows.json`, engine pin, landing download metadata, R7/R10
  safety evidence, or physical FAT acceptance.

A normal `workflow_dispatch` with **fat_preview disabled** continues the
existing installer-validation path. CI PR/push events remain read-only and
cannot publish any asset.

## Current qualification

The GR_X SCD/CSWI, Model Read diagnostics preview **#674** already has a
successful Windows Build and portable Actions ZIP. Exact tested source:
`7e93907e8ef1454913ac8b1b5d2f79e49d651bb6`, Build ARSAS run
`39719067914`. Its ZIP artifact contains the genuine self-contained
`.exe` but is **not** a direct GitHub Release asset yet. Do not claim a
`fat-7e93907e8ef1` release exists until the manually approved publication
successfully creates it.

The earlier standalone direct-EXE workflow proposed in #660 was blocked
by CI workflow/checkout budget; this implementation reuses the existing
installer lane instead of bypassing that invariant.
