from pathlib import Path

app_path = Path('App.xaml')
test_path = Path('tests/ARSAS.Tests/LiveMonitorRegressionAuditTests.cs')
app = app_path.read_text(encoding='utf-8')
test = test_path.read_text(encoding='utf-8')

anchor = '        <Geometry x:Key="LucideX">M18,6 L6,18 M6,6 L18,18</Geometry>\n'
addition = anchor + '        <Geometry x:Key="LucideSearch">M21,21 L16.65,16.65 M19,11 A8,8 0 1 1 3,11 A8,8 0 0 1 19,11 Z</Geometry>\n'
if app.count(anchor) != 1:
    raise SystemExit(f'LucideX anchor count={app.count(anchor)}')
app = app.replace(anchor, addition, 1)

old = '        Assert.Contains("ExplorerLiveSearchClear_Click", xaml, StringComparison.Ordinal);\n'
new = old + '        var app = File.ReadAllText(FindRepoFile("App.xaml"));\n        Assert.Contains("x:Key=\\\"LucideSearch\\\"", app, StringComparison.Ordinal);\n'
if test.count(old) != 1:
    raise SystemExit(f'test anchor count={test.count(old)}')
test = test.replace(old, new, 1)

app_path.write_text(app, encoding='utf-8', newline='\n')
test_path.write_text(test, encoding='utf-8', newline='\n')
print('Added reusable LucideSearch geometry and regression guard.')
