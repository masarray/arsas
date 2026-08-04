from pathlib import Path

path = Path(__file__).resolve().parents[1] / "scripts" / "validate-product-build.py"
text = path.read_text(encoding="utf-8")
old = 'PORTABLE = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip"'
new = 'PORTABLE = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.exe"'
if old not in text:
    raise SystemExit("Legacy rendered-site portable validation anchor was not found")
path.write_text(text.replace(old, new), encoding="utf-8")
print("Updated rendered-site trust validation for the portable single EXE.")
