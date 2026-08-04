from pathlib import Path

path = Path(__file__).resolve().parents[1] / ".github" / "workflows" / "publish-verified-release.yml"
text = path.read_text(encoding="utf-8")
old = '''    paths:
      - ".release/publish-verified.json"
      - ".github/workflows/publish-verified-release.yml"'''
new = '''    paths:
      - ".release/publish-verified.json"'''
if old not in text:
    raise SystemExit("Legacy publication workflow self-trigger anchor was not found")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("Removed the legacy release workflow self-trigger.")
