#!/usr/bin/env bash
set -euo pipefail

repo="repos/$GITHUB_REPOSITORY"
branch="fix/sas-operational-signal-profile"
git show origin/main:.github/workflows/build.yml > /tmp/production-build.yml
cat .staging/sas-operational/patch.00 \
    .staging/sas-operational/patch.01 \
    .staging/sas-operational/patch.02 \
    .staging/sas-operational/patch.03 \
    .staging/sas-operational/patch.04 \
    .staging/sas-operational/patch.05 \
    | base64 -d > /tmp/profile.patch.gz
echo "23cf060c940b54591f76f567c385767a2db4d90c1f92eb31ac2cc85e2d63e2c6  /tmp/profile.patch.gz" | sha256sum -c -
gzip -dc /tmp/profile.patch.gz > /tmp/profile.patch

python3 - <<'PY'
from pathlib import Path
for filename in [
    'App.xaml', 'MainWindow.xaml', 'MainWindow.xaml.cs',
    'Models/SignalDefinition.cs', 'SaveSclWindow.xaml', 'SaveSclWindow.xaml.cs',
    'Services/Iec61850MonitorRuntime.cs', 'Services/NativeIec61850Client.cs',
    'Services/NativeMmsDiscoveryMapper.cs', 'Services/SclWorkspaceSignalMapper.cs'
]:
    path = Path(filename)
    path.write_text(path.read_text(encoding='utf-8'), encoding='utf-8', newline='\n')
PY
git apply --ignore-space-change --ignore-whitespace /tmp/profile.patch

python3 - <<'PY'
from pathlib import Path
import xml.etree.ElementTree as ET

project = Path('ArIED61850Tester.csproj')
text = project.read_text(encoding='utf-8')
text = text.replace('<Version>1.6.13</Version>', '<Version>1.6.16</Version>')
text = text.replace('<AssemblyVersion>1.6.13.0</AssemblyVersion>', '<AssemblyVersion>1.6.16.0</AssemblyVersion>')
text = text.replace('<FileVersion>1.6.13.0</FileVersion>', '<FileVersion>1.6.16.0</FileVersion>')
project.write_text(text, encoding='utf-8', newline='\n')

publish = Path('scripts/publish-windows-portable.ps1')
text = publish.read_text(encoding='utf-8').replace('1.6.13', '1.6.16')
publish.write_text(text, encoding='utf-8', newline='\n')

workflow = Path('.github/workflows/build.yml')
workflow.write_bytes(Path('/tmp/production-build.yml').read_bytes())
text = workflow.read_text(encoding='utf-8')
text = text.replace('Verify premium P0, P1 and P2 UX invariants', 'Verify premium UX and SAS operational-signal invariants')
text = text.replace('1.6.13', '1.6.16')
read_anchor = '          $save = Get-Content .\\ArIED61850Tester\\SaveSclWindow.xaml -Raw\n'
read_block = read_anchor + (
    '          $project = Get-Content .\\ArIED61850Tester\\ArIED61850Tester.csproj -Raw\n'
    '          $publish = Get-Content .\\ArIED61850Tester\\scripts\\publish-windows-portable.ps1 -Raw\n'
    '          $signalDefinition = Get-Content .\\ArIED61850Tester\\Models\\SignalDefinition.cs -Raw\n'
    '          $nativeClient = Get-Content .\\ArIED61850Tester\\Services\\NativeIec61850Client.cs -Raw\n'
    '          $nativeMapper = Get-Content .\\ArIED61850Tester\\Services\\NativeMmsDiscoveryMapper.cs -Raw\n'
    '          $sclMapper = Get-Content .\\ArIED61850Tester\\Services\\SclWorkspaceSignalMapper.cs -Raw\n'
)
if read_anchor not in text:
    raise SystemExit('production workflow read anchor was not found')
text = text.replace(read_anchor, read_block, 1)

upload_anchor = '      - name: Upload source snapshot\n'
gate = r'''          if ($main -notmatch 'x:Name="WorkflowNavShell"[^>]*Height="48"' -or
              $main -notmatch 'Background="#D8E2F0"' -or
              $main -notmatch 'BorderBrush="#B7C6DA"') {
            throw "Ballistic navigation shell geometry or contrast regressed."
          }
          if ($main -match 'WorkflowPill' -or $main -match 'WorkflowPillTranslate') {
            throw "Legacy sliding navigation pill returned."
          }
          if ($signalDefinition -notmatch 'IsSasOperationalSignal' -or
              $signalDefinition -notmatch 'IsSasOperationalControl' -or
              $signalDefinition -notmatch 'IsSasVisibleSignal' -or
              $signalDefinition -notmatch 'dataObject is "mod" or "beh"' -or
              $signalDefinition -notmatch '\\.op\\.general' -or
              $signalDefinition -notmatch '\\.str\\.general' -or
              $signalDefinition -notmatch 'IsDefaultScadaMeasurementMagnitude') {
            throw "Typed SAS operational signal policy is incomplete."
          }
          if ($nativeClient -notmatch 'IsExactLiveDataAttributeTarget' -or
              $nativeClient -notmatch 'SAS operational points=' -or
              $nativeClient -notmatch 'IsOperationalSasDiscoverySignal' -or
              $nativeMapper -notmatch 'Native MMS inferred leaf candidate' -or
              $nativeMapper -notmatch 'IsSasVisibleSignal' -or
              $sclMapper -notmatch 'Where\(signal => signal\.IsSasVisibleSignal\)') {
            throw "Discovery or SCL projection is not enforcing exact SAS operational leaves."
          }
          if ($project -notmatch '<Version>1\.6\.16</Version>' -or
              $project -notmatch '<AssemblyVersion>1\.6\.16\.0</AssemblyVersion>' -or
              $publish -notmatch '1\.6\.16') {
            throw "Release metadata is not aligned to 1.6.16."
          }

'''
if upload_anchor not in text:
    raise SystemExit('production workflow upload anchor was not found')
text = text.replace(upload_anchor, gate + upload_anchor, 1)
workflow.write_text(text, encoding='utf-8', newline='\n')

for filename in ('App.xaml', 'MainWindow.xaml', 'SaveSclWindow.xaml'):
    ET.parse(filename)

required = {
    'Models/SignalDefinition.cs': ('IsSasVisibleSignal', 'IsOperationalSasControl'),
    'Services/NativeIec61850Client.cs': ('IsExactLiveDataAttributeTarget', 'SAS operational points='),
    'Services/NativeMmsDiscoveryMapper.cs': ('Native MMS exact value leaf', 'Native MMS inferred leaf candidate'),
    'Services/SclWorkspaceSignalMapper.cs': ('IsSasVisibleSignal',),
    'docs/SAS_OPERATIONAL_SIGNAL_PROFILE.md': ('exact, operational value leaves',),
}
for filename, tokens in required.items():
    body = Path(filename).read_text(encoding='utf-8')
    for token in tokens:
        if token not in body:
            raise SystemExit(f'missing {token!r} in {filename}')
PY

base_commit=$(git rev-parse HEAD)
base_tree=$(gh api "$repo/git/commits/$base_commit" --jq .tree.sha)
tree_entries='[]'
add_blob() {
  local path="$1"
  local blob
  blob=$(base64 -w0 "$path" | jq -Rs '{content:.,encoding:"base64"}' | gh api --method POST "$repo/git/blobs" --input - --jq .sha)
  tree_entries=$(jq --arg path "$path" --arg sha "$blob" '. + [{path:$path,mode:"100644",type:"blob",sha:$sha}]' <<<"$tree_entries")
}
for path in \
  .github/workflows/build.yml \
  App.xaml \
  ArIED61850Tester.csproj \
  MainWindow.xaml \
  MainWindow.xaml.cs \
  Models/SignalDefinition.cs \
  SaveSclWindow.xaml \
  SaveSclWindow.xaml.cs \
  Services/Iec61850MonitorRuntime.cs \
  Services/NativeIec61850Client.cs \
  Services/NativeMmsDiscoveryMapper.cs \
  Services/SclWorkspaceSignalMapper.cs \
  docs/SAS_OPERATIONAL_SIGNAL_PROFILE.md \
  scripts/publish-windows-portable.ps1; do
  add_blob "$path"
done
for path in \
  .github/workflows/apply-sas-push.yml \
  scripts/ci/apply-sas-profile.sh \
  .staging/sas-operational/trigger.txt \
  .staging/sas-operational/patch.00 \
  .staging/sas-operational/patch.01 \
  .staging/sas-operational/patch.02 \
  .staging/sas-operational/patch.03 \
  .staging/sas-operational/patch.04 \
  .staging/sas-operational/patch.05; do
  tree_entries=$(jq --arg path "$path" '. + [{path:$path,mode:"100644",type:"blob",sha:null}]' <<<"$tree_entries")
done
tree_sha=$(jq -n --arg base_tree "$base_tree" --argjson tree "$tree_entries" '{base_tree:$base_tree,tree:$tree}' | gh api --method POST "$repo/git/trees" --input - --jq .sha)
commit_sha=$(jq -n --arg tree "$tree_sha" --arg parent "$base_commit" '{message:"feat: expose exact SAS operational signals only",tree:$tree,parents:[$parent]}' | gh api --method POST "$repo/git/commits" --input - --jq .sha)
jq -n --arg sha "$commit_sha" '{sha:$sha,force:false}' | gh api --method PATCH "$repo/git/refs/heads/$branch" --input - >/dev/null
echo "Published $commit_sha"
