# Tracked asset provenance register

Status: **81 tracked binary asset paths inventoried for 25 October 2026; origin/rights, sanitization and visual-similarity reviews remain distinct from binary identity checks**.

This register supports the [independent implementation and provenance policy](INDEPENDENT_IMPLEMENTATION_AND_PROVENANCE.md), [clean-room policy](CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md), and [third-party notices](../THIRD_PARTY_NOTICES.md). It does **not** certify originality, license clearance, or freedom from resemblance to unrelated products.

## Audited snapshot

- Repository: `masarray/arsas`; historical baseline `b953612b535445095c808f164bebfaa4bbcbb203` (2026-09-25), updated asset inventory including v1.6.42 operator screenshots at branch commit `45838496e5dd289b42fbb731a6cafd8cd3547c32` (2026-10-25).
- Historic recursive tree response: not truncated; 1,030 tracked entries in the original snapshot. The live CI gate independently compares all currently tracked asset blobs against this revised manifest.
- Current extension inventory: **81** tracked `.png`, `.jpg`/`.jpeg`, `.webp`, `.ico`, `.svg`, `.ttf`, `.otf`, `.woff2` or `.gif` asset paths, including 8 authentic v1.6.42 operator PNG originals and 8 optimized WebP derivatives.
- This is a *path and Git-blob metadata inventory*, not an image-content, source-license, hidden-metadata, or legal review. The separately maintained source-clean gate scans tracked paths and supported text contents; it does not establish binary-image provenance.
- The machine-readable [per-file asset manifest](asset-provenance-manifest.json) records all **81** tracked asset paths, Git blob SHA, size, duplicate relationships and review status. There are **58 distinct Git blobs**. The 16 new screenshots are unique identities; existing 23 exact-duplicate relationships are preserved.
- CI validates that every tracked asset in this extension scope is represented and that its blob SHA still matches the manifest. Changing or adding an asset therefore requires an explicit provenance-manifest update rather than silently entering the tree.
- Inter v4.1 font provenance was separately checked against the official upstream release identity plus independent public package/hash records. This establishes technical byte provenance for the four bundled static faces; it is not a legal-clearance or originality opinion.

| Category | Count | Scope | Origin/rights disposition |
| --- | ---: | --- | --- |
| Bundled typeface faces | 4 | `Assets/Fonts/*.ttf` | Inter 4.1 release/archive and all four static-face SHA-256 values were technically corroborated on 2026-09-25; complete OFL-1.1 text is bundled. Re-verify upstream package/source whenever the dependency or bytes change. |
| Application artwork | 8 | Other visual files under `Assets/`, including icons, relay fascia and social image | Contributor/creator, source, license or own-work declaration and generation history need a separately recorded review for each distinct work. |
| Application screenshots | 25 | `Assets/screenshot/` | Verify capture comes from ARSAS, was generated with synthetic/sanitized data, and contains no confidential or unrelated-product image. Filename alone is not proof. |
| Website artwork | 4 | `landing/assets/` outside screenshots | Review original source and distribution rights; where Git blobs match an application asset, share its review record rather than claiming independent creation. |
| Website screenshots | 40 | `landing/assets/screenshots/` | Eight original PNG captures supplied and approved for publication by the operator (2026-10-25) plus eight derived WebP files; independent field-site data sanitization review is not asserted. Older captures retain their historical labels. |

The snapshot contains **23 groups of exact matching Git blob IDs** across app and website asset paths. Matching blob IDs prove byte-for-byte identity in that Git tree; they do not prove rights, authorship, or visual independence. The reference artwork and screenshot categories are intentionally counted by *tracked path*, not deduplicated distinct works.


## October 25, 2026 operator screenshot provenance

- Eight captures from the publicly published **ARSAS v1.6.42 Windows application** were provided directly by the operator for website use: IED Browser, Event Logs, Signal Testing, FAT PDF preview, SMV, GOOSE Subscriber, Alarm Annunciator, and Diagnostics.
- The **original PNG image bytes** are preserved under `landing/assets/screenshots/arsas-*-v1.6.42.png` and the SHA-identifiable Git blobs are recorded in the manifest. Matching full-resolution WebP files were derived without image compositing at quality 86 using Pillow, with original aspect ratios preserved.
- The files include sample device names and private-network IP addresses as captured. Public-upload approval is based on the contributor's explicit request; that does **not** represent an independent customer-information, equipment-location, or copyright clearance audit.
- The capture demonstrates UI and bounded operator testing, not R7/R10 physical acceptance, IEC 61850 certification or lossless buffered SOE. The previous v1.6.40 screenshots are retained for SCL/COMTRADE and historic setup screens where no newer capture was provided.

## Specific review queue

| Item | Git blob at audited snapshot | Review action |
| --- | --- | --- |
| `Assets/gateway-hero.png`, `Assets/ied-protection-relay-fascia.png`, relay fascia SVGs and social artwork | See the current tracked Git tree | Obtain creation/source records and inspect for third-party marks or visual copying. |
| App and landing screenshots | Exact pairs identified by matching Git blob IDs | Review unique image contents once, then record each deployment path and sanitization/redistribution scope. |

Two unreferenced legacy PNG outputs were removed after repository-wide reference checks found no active source, documentation, website, test, or workflow consumer. Active UI/release assets were not removed. Preserving any remaining file likewise does not mean its origin is certified. A reviewer should record the author/source, original or third-party license, whether the source was lawfully obtained, generation/reconstruction steps, allowed distribution scope, sanitization, visual similarity review, reviewer, date, and a content hash for each distinct asset. Unresolved or unsuitable assets should not enter future release packages.

## Boundaries

The separately pinned ARIEC61850 engine and ArdIrec native bridge are dependencies whose source revision and license require their own release evidence. The Inter typeface must retain its OFL notice. Black-box interoperability measurements and immutable historical commits are engineering provenance; do not rewrite their attribution or release SHA merely to eliminate an external name.

A passing source-clean gate or a clean inventory is **not** an independent copyright or trade-dress opinion. Preserve v1.6.40 tag, published assets and original field acceptance records unchanged. Re-run the inventory against the exact candidate Git tree before a future release and document any additions and resolved manual-review items.
