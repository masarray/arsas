from pathlib import Path

path = Path("SignalSelectionWizardWindow.xaml")
text = path.read_text(encoding="utf-8")

old = '''                        <Button x:Name="GlobalSearchClearButton"\n                                Grid.Column="2"\n                                Style="{StaticResource GlobalSearchClearButton}"\n                                Click="ClearGlobalFilter_Click"'''
new = '''                        <Button x:Name="GlobalSearchClearButton"\n                                Grid.Column="2"\n                                Click="ClearGlobalFilter_Click"'''

if old in text:
    text = text.replace(old, new, 1)
elif new not in text:
    raise SystemExit("Modern global-search clear button was not found in expected shape.")

# The local style inherits the reusable visual style and owns only the visibility trigger.
if '<Button.Style>' not in text or 'BasedOn="{StaticResource GlobalSearchClearButton}"' not in text:
    raise SystemExit("Expected local Button.Style/visibility trigger is missing.")
if 'Data="{StaticResource LucideX}"' not in text:
    raise SystemExit("Expected LucideX vector icon is missing.")
if 'Content="×"' in text:
    raise SystemExit("Legacy text multiplication-sign clear glyph is still present.")

path.write_text(text, encoding="utf-8", newline="\n")
print("Validated modern Lucide clear button and removed duplicate Style assignment.")
