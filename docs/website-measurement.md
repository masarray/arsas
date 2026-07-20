# ARSAS website measurement

ARSAS uses a lightweight, evidence-oriented measurement pipeline. Runtime analytics are optional and remain disabled when no valid measurement ID is configured. Even when configured, Google Analytics is denied by default and its client is not loaded until the visitor explicitly allows optional analytics. Search and performance reports are private GitHub Actions artifacts; they are not deployed to the public website.

## What is measured

| Question | Source | Output |
|---|---|---|
| Which pages are visited most? | GA4 `page_view` | Page path, views and active users |
| Which queries bring users? | Google Search Console | Query, clicks, impressions, CTR and average position |
| Which download buttons are clicked? | GA4 events | `download_installer`, `download_portable`, `download_checksums` |
| English vs Indonesian traffic | Page registry + GA4/Search Console | Views, search impressions and clicks by site language |
| Which pages have high impressions but low clicks? | Search Console | Pages and queries above the configured opportunity thresholds |
| Are links broken or 404s occurring? | Build-time crawler + deployed probe + GA4 | Missing files/fragments, HTTP failures, 404 paths and referrers |
| Are Core Web Vitals healthy? | Browser PerformanceObserver + PageSpeed/CrUX | LCP, CLS and INP field/lab evidence |

The browser client disables Google advertising signals, does not request ad-personalization signals, respects `Do Not Track`, loads asynchronously and performs no network request when the measurement ID is absent or consent has not been granted.

## Consent and privacy contract

- Default Google consent state: `analytics_storage=denied`.
- Advertising storage, ad-user-data and ad-personalization remain denied.
- The public GA4 client is dynamically loaded only after **Allow analytics**.
- Declining analytics does not change downloads, content access or application behavior.
- The local key `arsas_analytics_consent_v1` stores only `granted` or `denied`.
- Do Not Track overrides a stored grant and keeps analytics disabled.
- Revoking consent on a product page reloads that page to stop the already-loaded client before further interaction.
- `privacy.html` and `privasi.html` are bilingual `noindex,follow` preference surfaces. They carry only an inert availability configuration, can save the user’s choice, and never load `analytics.js` or Google Tag.
- A preference saved on a privacy page takes effect when the next product page is opened.
- Project policy requires a two-month GA4 event-data retention window before analytics is enabled.

## Repository configuration

Configure these **Actions variables**:

- `GA4_MEASUREMENT_ID`: public web stream ID such as `G-XXXXXXXXXX`. Leaving it empty keeps client measurement disabled and suppresses the first-visit consent prompt.
- `GA4_PROPERTY_ID`: numeric GA4 property ID used by the private reporting workflow.
- `GSC_SITE_URL`: the exact verified Search Console property, normally `https://masarray.github.io/arsas/` for a URL-prefix property.
- `PAGESPEED_URLS`: optional comma-separated URLs. When omitted, the workflow checks the English and Indonesian home/download pages plus Smart Reporting and Guides.

Configure these **Actions secrets**:

- `GOOGLE_SERVICE_ACCOUNT_JSON`: JSON for a service account with read-only access to the GA4 property and Search Console property. A `base64:<payload>` value is also accepted.
- `PAGESPEED_API_KEY`: optional PageSpeed Insights API key. The report still attempts the public endpoint when this secret is absent.

Grant the service account Viewer/read access only. It does not need permission to modify analytics, Search Console, releases or the website.

Before setting `GA4_MEASUREMENT_ID`, verify in GA4 that event-level retention is two months, Google Signals is disabled, Google Ads is not linked for this project, and no User-ID collection is configured.

## Production deployment attestation

Every Pages artifact is stamped after build with:

- full source commit SHA;
- source ref;
- source commit timestamp;
- GitHub Actions workflow run ID and attempt;
- stable release version;
- privacy and measurement activation state.

After `actions/deploy-pages`, `scripts/verify-pages-deployment.py` fetches the public `build-info.json` with cache-busting parameters and retries until the public source commit matches the just-deployed commit. It also verifies both privacy routes, their inert measurement-availability configuration, the absence of any analytics client on those policy pages, denied-by-default consent metadata, the configured measurement state and the shared consent controls on the homepage.

A stale public build, missing privacy route, unexpected analytics client or measurement-state mismatch fails the workflow. IndexNow notification depends on this attestation and is skipped when production is stale.

The `production-pages-attestation` artifact retains Markdown and JSON evidence for 90 days.

## Workflow behavior

`.github/workflows/site-measurement.yml` runs:

- on relevant pull requests and pushes: deterministic build, privacy generation, deployment stamping, consent/measurement validation, internal links and fragment validation;
- every Monday at 03:17 UTC, or manually: deployed page checks, official release-asset checks, an intentional 404 probe, GA4 aggregate reports, Search Console reports and PageSpeed/CrUX collection.

Artifacts:

- `site-measurement-quality`: local link, privacy and instrumentation evidence, retained for 30 days;
- `site-measurement-<run>`: private Markdown/JSON traffic, search, 404 and Core Web Vitals evidence, retained for 90 days;
- `production-pages-attestation`: deployed-commit and privacy evidence, retained for 90 days.

The same Markdown report is written to the GitHub Actions job summary.

## Opportunity rules

The initial low-CTR queue is deliberately conservative:

- query opportunity: at least 50 impressions, CTR below 3%, average position 20 or better;
- page opportunity: at least 100 impressions, CTR below 3%, average position 20 or better.

These thresholds are implemented in `scripts/build-site-measurement-report.py` and can be adjusted after several reporting cycles establish a stable baseline.

## Event contract

After consent, the local `landing/analytics.js` client emits:

- `page_view`;
- `page_not_found`;
- `language_switch`;
- `download_installer`;
- `download_portable`;
- `download_checksums`;
- `web_vital_lcp`;
- `web_vital_cls`;
- `web_vital_inp`;
- diagnostic `web_vital_ttfb`.

Every event carries page path, page title, site language, content group and stable release version. Download events also carry the official file name, destination URL and visible link text.

## Interpreting Core Web Vitals

Browser RUM events provide continuous observations from consented visits. The scheduled PageSpeed report remains the decision source for field CWV because it uses CrUX data when enough real-user samples exist. When CrUX has insufficient traffic, the report retains Lighthouse lab values and marks field data unavailable instead of inventing a pass/fail result.

## Continuous-improvement loop

1. Confirm the latest `production-pages-attestation` matches `main` before interpreting any website metric.
2. Review the weekly job summary.
3. Repair any broken internal link or failed 404 behavior immediately.
4. Prioritize high-impression pages with low CTR for title, description and intent alignment.
5. Compare English and Indonesian traffic before deciding which translations to expand.
6. Trace download clicks back to the page that generated them.
7. Investigate repeated 404 paths and add a valid route or redirect where appropriate.
8. Treat poor LCP, CLS or INP as a release-quality issue, then confirm the improvement in the next field-data cycle.
