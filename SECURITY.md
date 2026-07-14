# Security Policy

## Supported code

Security fixes are applied to the current `main` branch. Historical archive branches preserve earlier licensing and provenance boundaries but are not maintained as supported security release lines.

## Reporting a vulnerability

Do not publish an issue containing an exploitable vulnerability, credential, private endpoint, customer configuration, or confidential SCL/diagnostic material.

Use GitHub private vulnerability reporting when it is enabled for this repository. When that channel is unavailable, open a minimal public issue requesting a private contact channel without including sensitive technical details.

A useful report contains:

- the affected commit or version;
- the affected workflow or component;
- reproducible steps using synthetic data;
- expected and observed behavior;
- realistic impact;
- whether active network access, an IED, or operator interaction is required;
- a proposed mitigation when known.

## Security scope

Examples that belong in a security report include:

- unintended command dispatch or bypass of a confirmation/readiness guard;
- unsafe persistence or reuse of control-session state across IED associations;
- malformed network input causing code execution, data disclosure, or persistent denial of service;
- credential, path, or confidential project-data disclosure;
- untrusted project/SCL content escaping its intended parser or storage boundary;
- package or release tampering;
- dependency vulnerabilities that are reachable in the distributed application.

Normal connection failures, unsupported IED behavior, model mismatch, report allocation rejection, and interoperability problems should normally be reported as regular engineering issues unless they create a security impact.

## Operational technology boundary

ArIED can perform active IEC 61850 operations, including control requests and report-related writes. Software readiness does not establish switching authority, equipment isolation, functional safety, cybersecurity approval, or permission to connect to an operational substation network.

Use active functions only in an approved laboratory or commissioning boundary with:

- an authorized test plan;
- confirmed network and equipment isolation as required by site procedure;
- independently verified target IED identity;
- validated positive and negative control paths;
- qualified supervision appropriate to the equipment risk.

## Sensitive-data handling

Before sharing logs, screenshots, SCL files, diagnostics, or project files, remove:

- customer, employer, station, bay, and feeder names;
- credentials, tokens, certificate material, and private keys;
- non-public IP addressing and network topology;
- serial numbers and asset identifiers;
- proprietary configuration or vendor support material;
- personal information;
- any content restricted by contract or organizational policy.

Prefer synthetic reproductions over production artifacts.
