# ARSAS production and measured-growth operations

P2 closes two operational gaps: proving that GitHub Pages serves the newest deployable website commit, and making private measurement activation explicit instead of treating an unconfigured report as a successful live report.

## Deployable website source

The repository HEAD is not always the expected Pages source. Application-only commits do not change the website artifact.

`scripts/resolve-site-source-commit.py` walks complete Git history and resolves the newest commit that changes the website source, build, validation, privacy, authority, indexing or Pages workflow contract. Scheduled production verification compares public `build-info.json` with that commit rather than blindly comparing it with repository HEAD.

This prevents false stale-deployment alarms after normal application development.

## Self-healing production health

`.github/workflows/production-health.yml` runs every six hours and on manual dispatch.

1. Resolve the latest deployable site source commit.
2. Run the public source, privacy, measurement and authority attestation.
3. When the first attestation fails, trigger one `Deploy product website` workflow dispatch.
4. Poll the public site again for up to six minutes.
5. Store the complete attestation as a private 90-day artifact.
6. Open or update one persistent GitHub issue when recovery fails.
7. Close the issue automatically after a later successful attestation.

The workflow performs only one automatic repair attempt per run. It does not loop deployments indefinitely and does not change Pages, DNS or repository settings automatically.

## Measurement readiness states

`scripts/check-measurement-readiness.py` produces private Markdown and JSON evidence without printing credentials.

Possible states:

- `disabled`: no public collection or private Google reporting configuration;
- `collection-only`: a valid public GA4 measurement ID exists, but private reporting is not authenticated;
- `private-reporting-ready`: Search Console and/or GA4 reporting is authenticated, while browser collection can remain disabled;
- `fully-ready`: consent-gated public collection, GA4 reporting and Search Console reporting are all ready;
- `partial` or `invalid`: configuration is inconsistent and must be corrected.

Required repository variables:

- `GA4_MEASUREMENT_ID`: public GA4 web-stream ID, for example `G-XXXXXXXXXX`;
- `GA4_PROPERTY_ID`: numeric GA4 property ID;
- `GSC_SITE_URL`: exact URL-prefix property `https://masarray.github.io/arsas/`;
- optional `PAGESPEED_URLS`;
- optional `GCP_WORKLOAD_IDENTITY_PROVIDER` and `GCP_SERVICE_ACCOUNT` for keyless authentication.

Authentication can use either:

- preferred Workload Identity Federation through `google-github-actions/auth`; or
- the existing `GOOGLE_SERVICE_ACCOUNT_JSON` Actions secret as a compatibility path.

Generated `gha-creds-*.json` files are ignored by Git.

## Live measurement evidence

`scripts/build-site-measurement-report.py` now records:

- GA4 pages, users, consented download events and consented 404 events;
- current-window versus previous-window GA4 trends;
- paginated Search Console query and page data;
- Search Console data-presence status;
- current-window versus previous-window clicks, impressions, CTR and average position;
- submitted sitemap status, last download, warnings, errors and processing state;
- English versus Indonesian traffic and search visibility;
- PageSpeed and CrUX evidence.

Search Console results can be `available-no-data`. That means authorization succeeded but finalized rows are not yet present; it is not treated as zero demand.

`scripts/validate-measurement-report.py` fails the scheduled report when a source declared ready cannot be queried, or when the canonical sitemap is missing, erroneous or warning-bearing. A newly submitted `pending` sitemap is allowed until the next cycle provides a stronger result.

## Growth queue

The private growth queue now prioritizes:

- sitemap errors before page-level SEO changes;
- observed 404 paths;
- major search-click or impression declines;
- low-CTR pages and queries;
- Core Web Vitals failures;
- authority-depth and demand-led localization gaps.

The queue never creates pages automatically and never deploys traffic, query or credential data to the public website.
