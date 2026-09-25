# Tracked asset provenance register

Status: **inventory complete for the named Git snapshot; origin/rights and visual-similarity review remain open**.

This register supports the [independent implementation and provenance policy](INDEPENDENT_IMPLEMENTATION_AND_PROVENANCE.md), [clean-room policy](CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md), and [third-party notices](../THIRD_PARTY_NOTICES.md). It does **not** certify originality, license clearance, or freedom from resemblance to unrelated products.

## Audited snapshot

- Repository: `masarray/arsas`; tracked recursive Git tree at `61ad333c2f61784ee49fda04b47f036206265cbc` (2026-09-25).
- Full recursive tree response: not truncated; 1,028 tracked blobs.
- Extension inventory: 67 `.png`, `.jpg`/`.jpeg`, `.webp`, `.ico`, `.svg`, `.ttf`, `.otf`, `.woff2` or `.gif` blobs.
- This is a *path and Git-blob metadata inventory*, not an image-content, source-license, hidden-metadata, or legal review. The separately maintained source-clean gate scans tracked paths and supported text contents; it does not establish binary-image provenance.
- The machine-readable [per-file asset manifest](asset-provenance-manifest.json) records all 67 tracked asset paths, their Git blob SHA, byte size, exact-duplicate relationship and explicit review status. There are 44 unique asset blobs; 23 duplicate groups are byte-for-byte deployments of the same Git blob at two paths.
- CI validates that every tracked asset in this extension scope is represented and that its blob SHA still matches the manifest. Changing or adding an asset therefore requires an explicit provenance-manifest update rather than silently entering the tree.
- Inter v4.1 font provenance was separately checked against the official upstream release identity plus independent public package/hash records. This establishes technical byte provenance for the four bundled static faces; it is not a legal-clearance or originality opinion.

| Category | Count | Scope | Origin/rights disposition |
| --- | ---: | --- | --- |
| Bundled typeface faces | 4 | `Assets/Fonts/*.ttf` | Inter 4.1 release/archive and all four static-face SHA-256 values were technically corroborated on 2026-09-25; complete OFL-1.1 text is bundled. Re-verify upstream package/source whenever the dependency or bytes change. |
| Application artwork | 8 | Other visual files under `Assets/`, including icons, relay fascia and social image | Contributor/creator, source, license or own-work declaration and generation history need a separately recorded review for each distinct work. |
| Application screenshots | 25 | `Assets/screenshot/` | Verify capture comes from ARSAS, was generated with synthetic/sanitized data, and contains no confidential or unrelated-product image. Filename alone is not proof. |
| Website artwork | 4 | `landing/assets/` outside screenshots | Review original source and distribution rights; where Git blobs match an application asset, share its review record rather than claiming independent creation. |
| Website screenshots | 24 | `landing/assets/screenshots/` | Review capture and sanitization; associate identical Git blobs with application screenshot records. |
| Legacy output visuals | 2 | `output/scl-signal-selection-comparison.png`, `output/scl-signal-selection-modern.png` | **Manual review required** before reuse, publication or release packaging. Their names/history do not establish ownership or that the images contain third-party content. |

The snapshot contains **23 groups of exact matching Git blob IDs** across app and website asset paths. Matching blob IDs prove byte-for-byte identity in that Git tree; they do not prove rights, authorship, or visual independence. The reference artwork and screenshot categories are intentionally counted by *tracked path*, not deduplicated distinct works.

## Specific review queue

| Item | Git blob at audited snapshot | Review action |
| --- | --- | --- |
| `output/scl-signal-selection-comparison.png` | `c3a6ff981b40eb6878da5349fe80d1fbe9eab09f` | Inspect the actual image, identify its creator and all depicted UI/assets, verify redistribution rights, then decide retain/recreate/remove. Do not infer content from filename. |
| `output/scl-signal-selection-modern.png` | `4f0acfe082833972f84de58c74280ad86991cbd3` | Apply the same review, and check whether both images are still consumed by documentation or workflows. |
| `Assets/gateway-hero.png`, `Assets/ied-protection-relay-fascia.png`, relay fascia SVGs and social artwork | See the current tracked Git tree | Obtain creation/source records and inspect for third-party marks or visual copying. |
| App and landing screenshots | Exact pairs identified by matching Git blob IDs | Review unique image contents once, then record each deployment path and sanitization/redistribution scope. |

**No binary asset was removed or relabeled in this inventory change.** Removing an active UI image without first tracing its consumers is a potential product regression; preserving a file likewise does not mean its origin is certified. A reviewer should record the author/source, original or third-party license, whether the source was lawfully obtained, generation/reconstruction steps, allowed distribution scope, sanitization, visual similarity review, reviewer, date, and a content hash for each distinct asset. Unresolved or unsuitable assets should not enter future release packages.

## Boundaries

The separately pinned ARIEC61850 engine and ArdIrec native bridge are dependencies whose source revision and license require their own release evidence. The Inter typeface must retain its OFL notice. Black-box interoperability measurements and immutable historical commits are engineering provenance; do not rewrite their attribution or release SHA merely to eliminate an external name.

A passing source-clean gate or a clean inventory is **not** an independent copyright or trade-dress opinion. Preserve v1.6.40 tag, published assets and original field acceptance records unchanged. Re-run the inventory against the exact candidate Git tree before a future release and document any additions and resolved manual-review items.
