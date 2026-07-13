from pathlib import Path

path = Path(__file__).resolve().parents[1] / "MainWindow.xaml.cs"
raw = path.read_bytes()
newline = "\r\n" if b"\r\n" in raw else "\n"
text = raw.decode("utf-8").replace("\r\n", "\n")

old_empty = '''                    $"No usable IEC 61850 MMS endpoint was found in {sourceName}.

{reason}

The file may contain only an IED template without a Communication section.",'''
new_empty = '''                    $"No usable IEC 61850 MMS endpoint was found in {sourceName}.\\n\\n{reason}\\n\\nThe file may contain only an IED template without a Communication section.",'''

old_error = '''                $"ArIED could not read this SCL file.

{ex.Message}",'''
new_error = '''                $"ArIED could not read this SCL file.\\n\\n{ex.Message}",'''

for old, new, label in (
    (old_empty, new_empty, "empty endpoint dialog"),
    (old_error, new_error, "error dialog"),
):
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    text = text.replace(old, new, 1)

path.write_bytes(text.replace("\n", newline).encode("utf-8"))
print("SCL dialog string literals repaired")
