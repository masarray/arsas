## Engineering problem

Describe the IEC 61850, application, UX, packaging, documentation, or validation problem this pull request addresses.

## Solution

Explain what changed and why this approach belongs in ARSAS, ARIEC61850, or both.

## Capability maturity

- [ ] Existing available capability remains available
- [ ] Engineering-preview capability
- [ ] New available capability with completed validation
- [ ] Roadmap or documentation only
- [ ] No public capability claim changed

## Validation

List the checks performed and their results.

- [ ] Source and provenance gate
- [ ] Landing-page validation, when applicable
- [ ] .NET restore and Release build
- [ ] Relevant unit or deterministic fixture tests
- [ ] Simulator or loopback validation
- [ ] Authorized laboratory IED validation
- [ ] Negative and failure-path validation
- [ ] Windows installer or portable-package validation
- [ ] Performance or allocation measurements, when applicable

Provide details:

```text
Commands, environment, test boundary, result, and known limitations
```

## IEC 61850 evidence

For MMS, reporting, GOOSE, Sampled Values, file transfer, SCL, or control changes, describe:

- affected services, models, or control blocks;
- expected and observed behavior;
- timestamp, quality, sequence, error, or termination evidence;
- unsupported or intentionally bounded behavior;
- ARSAS/ARIEC61850 contract changes.

## UI and accessibility

For UI changes, include:

- tested Windows scaling and resolution;
- keyboard workflow;
- screen-reader or accessible-name impact where relevant;
- responsiveness or rendering impact;
- screenshots using synthetic, non-confidential data only.

## Safety, security, and data handling

- [ ] No credentials, private endpoints, customer names, station identifiers, relay settings, packet captures, disturbance records, or confidential SCL/project data are included.
- [ ] Active testing, if any, occurred inside an approved boundary with the required authorization.
- [ ] The change does not imply switching authority, formal conformance, functional-safety certification, cybersecurity approval, or universal interoperability.
- [ ] Downloaded files and local paths are handled safely when file-transfer behavior is affected.

## Provenance and licensing

- [ ] The contribution is independently authored.
- [ ] No proprietary source, documentation wording, tests, UI, screenshots, or restricted engineering material was copied.
- [ ] Third-party components and licenses are identified.
- [ ] Public license, commercial-license, and trademark wording remains accurate.
- [ ] I have read and affirmatively agree to the [Contributor License Agreement](../CONTRIBUTOR-LICENSE-AGREEMENT.md) for this contribution, and I have the rights and any required authorization to submit it.

## Independent implementation and provenance

- [ ] Product-facing terminology and synthetic code/test identifiers are ARSAS-owned or neutral.
- [ ] Any external comparison is authorized black-box evidence with its original provenance retained; no source, assets, UI or documentation were copied.
- [ ] New/generated fixtures have an identified author or generator; third-party assets have verified licenses and notices.
- [ ] Existing field evidence, DataSet order, release/tag/engine SHA and failed-case history remain unchanged unless a separate evidence-backed change explicitly supersedes them.
- [ ] Rename or JSON-schema changes update all consumers and preserve numerical/semantic assertions.

See [independent implementation and provenance policy](../docs/INDEPENDENT_IMPLEMENTATION_AND_PROVENANCE.md).

## Documentation

- [ ] README, website, roadmap, support, security, or engineering documentation was updated where behavior or claim boundaries changed.
- [ ] No documentation update is required.

## Reviewer focus

Call out the highest-risk files, assumptions, or decisions that deserve focused review.
