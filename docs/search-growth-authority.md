# ARSAS search growth and authority

P1 turns the privacy-safe website measurement foundation into a controlled product-discovery loop.

## Contextual authority graph

`landing/search-authority.json` maps IEC 61850 capability pages, solution pages and troubleshooting guides into engineering clusters. Every mapped page receives two to four contextual evidence links before the Download CTA.

The graph must prove that every troubleshooting guide has at least one inbound path from a capability or solution page. Generic sitewide links and guide-only loops do not satisfy this contract.

`scripts/apply-search-authority.py` validates and renders the graph. The source graph is removed from the deployable artifact. `scripts/validate-search-authority.py` checks the rendered relationships, placement, inbound guide coverage and `build-info.json` evidence.

The post-deployment Pages attestation verifies the public authority metadata, probes the Smart Reporting relationship set and confirms that the source graph is not publicly served.

## Private growth queue

`scripts/build-search-growth-report.py` creates a prioritized private queue from:

- contextual inbound depth;
- English pages without an Indonesian alternate;
- Search Console low-CTR opportunities when evidence is available;
- consented 404 observations when evidence is available;
- PageSpeed or CrUX failures when evidence is available.

The queue does not create pages automatically. Queries must first be mapped to an existing evidence page to avoid thin or duplicate content. Localization gaps remain candidates until Indonesian demand justifies translation.

`.github/workflows/search-growth.yml` validates the structural queue on relevant changes and creates a measured private queue every Monday at 04:05 UTC or on manual dispatch. Structural artifacts are retained for 30 days and measured artifacts for 90 days.

Traffic, search queries and growth queues are never deployed to the public website.
