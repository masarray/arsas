# ARSAS Security Policy

## Supported code

Security fixes are applied to the current `main` branch. Historical archive branches preserve earlier licensing and provenance boundaries but are not maintained as supported security release lines.

## Reporting a vulnerability

Do not publish an issue containing an exploitable vulnerability, credential, private endpoint, customer configuration, relay setting, packet capture, file-service content, or confidential SCL/diagnostic material.

Use GitHub private vulnerability reporting when it is enabled for this repository. When that channel is unavailable, open a minimal public issue requesting a private contact channel without including sensitive technical details.

A useful report contains:

- the affected commit or version;
- the affected ARSAS or ARIEC61850 component;
- reproducible steps using synthetic data;
- expected and observed behavior;
- realistic impact and preconditions;
- whether active network access, an IED, crafted SCL/file content, or operator interaction is required;
- a proposed mitigation when known.

## Security scope

Examples that belong in a security report include:

- unintended command dispatch or bypass of a confirmation/readiness guard;
- unsafe persistence or reuse of control-session state across IED associations;
- malformed MMS, report, GOOSE, Sampled Values, file-service, project, or SCL input causing code execution, data disclosure, or persistent denial of service;
- path traversal or unsafe local file creation during fault-record download;
- credential, private endpoint, file path, or confidential project-data disclosure;
- untrusted project/SCL content escaping its intended parser or storage boundary;
- package, installer, update, or release tampering;
- dependency vulnerabilities that are reachable in the distributed application.

Normal connection failures, unsupported IED behavior, model mismatch, report allocation rejection, transfer failure, and interoperability problems should normally be reported as regular engineering issues unless they create a security impact.

## Operational-technology boundary

ARSAS can perform active IEC 61850 operations, including control requests, report-related writes, temporary DataSet configuration, and file-service access. Software readiness does not establish switching authority, equipment isolation, functional safety, cybersecurity approval, or permission to connect to an operational substation network.

Use active functions only in an approved laboratory or commissioning boundary with:

- an authorized test plan;
- confirmed network and equipment isolation as required by site procedure;
- independently verified target IED identity;
- validated positive and negative control paths;
- approved packet-capture and file-access boundaries;
- qualified supervision appropriate to the equipment risk.

## Sensitive-data handling

Before sharing logs, screenshots, SCL files, diagnostics, project files, captures, or downloaded records, remove:

- customer, employer, station, bay, feeder, IED, and project names;
- credentials, tokens, certificate material, and private keys;
- non-public IP addressing, VLAN information, and network topology;
- serial numbers and asset identifiers;
- relay settings, disturbance records, and proprietary configuration;
- vendor support material or licensed documentation;
- personal information;
- any content restricted by contract, law, or organizational policy.

Prefer synthetic reproductions over production artifacts.
