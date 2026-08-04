from pathlib import Path

root = Path(__file__).resolve().parents[1]
replacements = {
    "scripts/build-product-site.py": (
        '        ("portable", "ARSAS-Windows-x64-Portable.zip"),',
        '        ("portable", "ARSAS-Windows-x64-Portable.exe"),',
    ),
    "landing/site.json": (
        "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip",
        "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.exe",
    ),
}

for relative, (old, new) in replacements.items():
    path = root / relative
    text = path.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"Patch anchor missing in {relative}: {old}")
    path.write_text(text.replace(old, new), encoding="utf-8")

for relative in replacements:
    text = (root / relative).read_text(encoding="utf-8")
    if "ARSAS-Windows-x64-Portable.zip" in text:
        raise SystemExit(f"Legacy public portable identity remains in {relative}")

print("Updated product-site builder and download configuration for the v1.6.20 portable EXE.")
