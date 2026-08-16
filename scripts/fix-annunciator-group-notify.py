from pathlib import Path

path = Path("Models/AlarmAnnunciatorModels.cs")
text = path.read_text(encoding="utf-8")
old = '''        if (changed)\n        {\n            Raise(nameof(VisualState));\n            Raise(nameof(StatusText));\n        }\n        Raise(nameof(ConfiguredCount));\n        Raise(nameof(LampOpacity));'''
new = '''        if (changed)\n            Raise(nameof(VisualState));\n        Raise(nameof(ConfiguredCount));\n        Raise(nameof(StatusText));\n        Raise(nameof(LampOpacity));'''
if text.count(old) != 1:
    raise SystemExit(f"expected one group notification block, found {text.count(old)}")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
