#!/usr/bin/env python3
"""Generate an SPDX 2.3 JSON SBOM for an extracted ARSAS Windows package."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path


def file_hashes(path: Path) -> tuple[str, str]:
    sha1 = hashlib.sha1()
    sha256 = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            sha1.update(chunk)
            sha256.update(chunk)
    return sha1.hexdigest(), sha256.hexdigest()


def normalize_created(value: str) -> str:
    text = value.strip()
    if not text:
        return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    parsed = datetime.fromisoformat(text.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("created timestamp must include a timezone")
    return parsed.astimezone(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def spdx_id(relative: str) -> str:
    normalized = re.sub(r"[^A-Za-z0-9.-]+", "-", relative).strip("-")
    suffix = hashlib.sha256(relative.encode("utf-8")).hexdigest()[:12]
    return f"SPDXRef-File-{normalized[:80]}-{suffix}"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package-dir", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--created", default="", help="ISO-8601 source timestamp used for reproducible SBOM metadata")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    package_dir = Path(args.package_dir).resolve()
    output = Path(args.output).resolve()
    if not package_dir.is_dir():
        raise SystemExit(f"Package directory does not exist: {package_dir}")
    if not re.fullmatch(r"\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?", args.version):
        raise SystemExit("Version is not semantic")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", args.source_commit):
        raise SystemExit("Source commit must be a full Git SHA")
    try:
        created = normalize_created(args.created)
    except ValueError as exc:
        raise SystemExit(f"Invalid created timestamp: {exc}") from exc

    files = [path for path in sorted(package_dir.rglob("*")) if path.is_file()]
    if not files:
        raise SystemExit("Package directory contains no files")

    file_entries = []
    sha1_values: list[str] = []
    relationships = [
        {
            "spdxElementId": "SPDXRef-DOCUMENT",
            "relationshipType": "DESCRIBES",
            "relatedSpdxElement": "SPDXRef-Package-ARSAS",
        }
    ]
    for path in files:
        relative = path.relative_to(package_dir).as_posix()
        sha1_digest, sha256_digest = file_hashes(path)
        sha1_values.append(sha1_digest)
        identifier = spdx_id(relative)
        file_entries.append(
            {
                "SPDXID": identifier,
                "fileName": "./" + relative,
                "checksums": [
                    {"algorithm": "SHA1", "checksumValue": sha1_digest},
                    {"algorithm": "SHA256", "checksumValue": sha256_digest},
                ],
                "licenseConcluded": "NOASSERTION",
                "licenseInfoInFiles": ["NOASSERTION"],
                "copyrightText": "NOASSERTION",
            }
        )
        relationships.append(
            {
                "spdxElementId": "SPDXRef-Package-ARSAS",
                "relationshipType": "CONTAINS",
                "relatedSpdxElement": identifier,
            }
        )

    verification_code = hashlib.sha1("".join(sorted(sha1_values)).encode("ascii")).hexdigest()
    namespace_seed = hashlib.sha256(
        (args.version + args.source_commit.lower() + verification_code).encode("utf-8")
    ).hexdigest()
    document = {
        "spdxVersion": "SPDX-2.3",
        "dataLicense": "CC0-1.0",
        "SPDXID": "SPDXRef-DOCUMENT",
        "name": f"ARSAS-{args.version}-windows-x64",
        "documentNamespace": f"https://github.com/masarray/arsas/sbom/{args.version}/{namespace_seed}",
        "creationInfo": {
            "created": created,
            "creators": ["Tool: ARSAS generate-release-sbom.py", "Person: Ari Sulistiono"],
        },
        "documentDescribes": ["SPDXRef-Package-ARSAS"],
        "packages": [
            {
                "name": "ARSAS",
                "SPDXID": "SPDXRef-Package-ARSAS",
                "versionInfo": args.version,
                "downloadLocation": f"https://github.com/masarray/arsas/releases/tag/v{args.version}",
                "homepage": "https://masarray.github.io/arsas/",
                "sourceInfo": f"Built from Git commit {args.source_commit.lower()} in masarray/arsas.",
                "filesAnalyzed": True,
                "packageVerificationCode": {"packageVerificationCodeValue": verification_code},
                "licenseConcluded": "GPL-3.0-or-later",
                "licenseDeclared": "GPL-3.0-or-later",
                "copyrightText": "Copyright (C) 2026 Ari Sulistiono",
            }
        ],
        "files": file_entries,
        "relationships": relationships,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Generated SPDX 2.3 SBOM with {len(files)} files: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
