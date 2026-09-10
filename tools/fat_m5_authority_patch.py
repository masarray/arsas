from pathlib import Path


def replace_exact(path: Path, old: str, new: str, expected: int = 1) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != expected:
        raise SystemExit(f"{path}: expected {expected} occurrence(s), found {count}: {old[:80]!r}")
    path.write_text(text.replace(old, new), encoding="utf-8")


persistence = Path("Services/IoTesting/IoTestProjectPersistenceService.cs")
bootstrap = Path("Services/IoTesting/IoTestWorkspaceBootstrapService.cs")

replace_exact(
    persistence,
    """            {\n                LatestComtradeFiles = ied.LatestComtradeFiles,""",
    """            {\n                // M5: persist the stable Engineering DeviceId when available. Older snapshots\n                // omit this field and continue through the exact IEC identity + endpoint fallback.\n                LiveDeviceId = ied.LiveDeviceId,\n                LatestComtradeFiles = ied.LatestComtradeFiles,""",
)

replace_exact(
    persistence,
    """    {\n        public string LatestComtradeFiles { get; init; } = string.Empty;""",
    """    {\n        public string LiveDeviceId { get; init; } = string.Empty;\n        public string LatestComtradeFiles { get; init; } = string.Empty;""",
)

replace_exact(
    bootstrap,
    """            if (!currentIedIdentityCounts.TryGetValue(iedKey, out var currentIdentityCount) ||\n                currentIdentityCount != 1 ||\n                !savedIedsByIdentity.TryGetValue(iedKey, out var savedIed))""",
    """            if (!currentIedIdentityCounts.TryGetValue(iedKey, out var currentIdentityCount) ||\n                currentIdentityCount != 1 ||\n                !savedIedsByIdentity.TryGetValue(iedKey, out var savedIed) ||\n                !IoTestPerIedProgressIdentity.PersistedIedOwnershipMatches(ied, savedIed))""",
)

replace_exact(
    bootstrap,
    """            if (savedIed.ValueKind != JsonValueKind.Object)\n                continue;\n\n            var existingIds = ied.TestPoints""",
    """            if (savedIed.ValueKind != JsonValueKind.Object ||\n                !IoTestPerIedProgressIdentity.PersistedIedOwnershipMatches(ied, savedIed))\n            {\n                continue;\n            }\n\n            var existingIds = ied.TestPoints""",
)

replace_exact(
    bootstrap,
    """            if (saved.ValueKind != JsonValueKind.Object)\n                continue;\n\n            ied.LatestComtradeFiles""",
    """            if (saved.ValueKind != JsonValueKind.Object ||\n                !IoTestPerIedProgressIdentity.PersistedIedOwnershipMatches(ied, saved))\n            {\n                continue;\n            }\n\n            ied.LatestComtradeFiles""",
)

print("M5 persistence authority patch applied")
