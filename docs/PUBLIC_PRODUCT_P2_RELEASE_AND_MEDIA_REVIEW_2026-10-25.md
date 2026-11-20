# ARSAS Public Release and Screenshot Review — 2026-10-25

## Scope and immutable boundaries

This checkpoint follows the accepted public **v1.6.42** release and the merged P0/P1 landing work (#742/#743). It does **not** repackage the application, repoint `v1.6.42`, repin ARIEC61850, rewrite the Windows executable, certify IEC 61850 conformance, or approve physical R7/R10 promotion (#729).

**Source of truth for a downloadable release:** the published GitHub stable tag and immutable release assets, mirrored to `landing/latest.json` and `.release/published.json`. A different version on main/VERSION is intentionally possible pending physical authority. Do not change the stable ledgers to match an unqualified application tree.

## P2A — release contract hardened

- The public stable Portable artifact is `ARSAS-Windows-x64-Portable.exe` for **v1.6.42 and later**. Older *tag-specific* release ZIP links may be recognized as historical but must never be presented as the latest stable asset.
- Public analytics must report current portable EXE clicks; a historical ZIP may use a separate legacy event.
- The canonical 62-page rendered site validator is `scripts/validate-product-build.py`. The obsolete `scripts/validate-built-landing.py` command is only a compatibility forwarding entry point; do not resurrect its stale eight-page, Portable ZIP policy.
- `publish-verified-release.yml` remains a **legacy ZIP-format artifact publisher** with a version gate. It must reject a publication request for v1.6.42+ rather than quietly publish a ZIP under a modern tag. **The canonical modern publish lane remains `release-windows.yml`.**
- `release-supply-chain.yml` backfills *historical extractable ZIP* assets. It must fail closed for opaque single-EXE releases; don't invent an extracted EXE file inventory or replace an existing SBOM. Current v1.6.42 already has an SPDX SBOM and provenance from its canonical release.
- `scripts/validate-product-source.py` verifies exact modern asset name and download URL (not a permissive ZIP-or-EXE choice), while keeping legacy version handling explicit.

## P2B — public screenshot data safety (separate human review)

Eight user-supplied authentic v1.6.42 screenshots and their WebP counterparts are public: IED Browser, Event Logs, Signal Testing, FAT PDF Preview, SMV, GOOSE, Annunciator, Diagnostics.

**Known provenance:** #743 records operator approval to publish the supplied screenshot captures. The existing manifest `docs/asset-provenance-manifest.json` records image paths, Git blob IDs and byte sizes. This is **not** independent confirmation that every visible IP address, station/device label, log identifier, person or customer is harmless.

The Pages source gate `scripts/audit-public-media.py` checks the 16 current PNG/WebP assets for image-container EXIF/XMP/text chunks and malformed headers. Results are uploaded as `media-audit.json`. It reads but never alters source bytes. **This does not inspect rasterized pixels** and does not replace the required visual review.

### Human review acceptance checklist

- [ ] View all eight original **PNG** captures at full resolution and all eight **WebP** derivatives; check cropped/offscreen panes, popovers, titles, tabs and status bar.
- [ ] Assess every visible IP/MAC, VLAN, station/feeder/device label, engineer/customer name, serial/asset ID, filename, SCL/path, event/packet log, credentials/certificates, and topology clue for authorized public disclosure.
- [ ] Record reviewer, review date, each capture's exact SHA/blob reference and disposition in a separate review issue or signed off review document.
- [ ] If any pixel-level disclosure is not authorized, remove or redact **both** public derivatives through a separately reviewed change; update provenance and all references. Do not silently alter the original operator capture under the same SHA or claim it is unchanged.
- [ ] Confirm that screenshots demonstrate product UI only; no certification or universal compatibility claim is inferred from simulated/limited field test evidence.

**Review status:** publication approved by the operator; automated hidden-metadata audit added; **independent pixel/content sanitization NOT YET ASSERTED**. Keep this review open until a person with the authority to disclose the shown engineering data signs off. Do not mark it complete solely because CI is green.

## P4 — sitemap correction

The actual October 25 P1 content refresh modified EN/ID Quick Start, Demo, FAQ, and Compatibility pages. Their sitemap registry entries are stamped with **2026-10-25**. This fixes demonstrably stale `lastmod` dates without falsifying unrelated older pages. Subsequent SEO/mobile/accessibility measurement remains a separate review.

## Next phase gates

1. Complete and document the human screenshot disclosure review.
2. Audit all 62 rendered site pages for keyboard access, links, contrast, mobile breakpoints, and Core Web Vitals using measured evidence.
3. Simplify the beginner-first README and contribution paths without removing historical changelog, trust boundaries or Git provenance.
4. Keep PR #729 source qualification as a **separate** physical R7/R10 workstream. Mainline source promotion does not follow from website CI success.
