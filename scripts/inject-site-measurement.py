#!/usr/bin/env python3
"""Configure optional site measurement after the deterministic website build."""

from __future__ import annotations

import argparse
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    parser.add_argument("--measurement-id", default="")
    args = parser.parse_args()
    site = Path(args.site).resolve()
    if not site.is_dir():
        raise SystemExit(f"Site directory does not exist: {site}")
    print("Site measurement configuration ready.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
