#!/usr/bin/env python3
"""Compatibility entry point for the canonical rendered product-site validator.

The old eight-page validator and Portable.zip contract were superseded by the
62-page, single-EXE-aware scripts/validate-product-build.py. Keep this command
for documented downstream callers without maintaining two divergent policies.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path


def main() -> int:
    canonical = Path(__file__).resolve().with_name("validate-product-build.py")
    return subprocess.call([sys.executable, str(canonical), *sys.argv[1:]])


if __name__ == "__main__":
    raise SystemExit(main())
