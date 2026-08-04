from pathlib import Path

root = Path(__file__).resolve().parents[1]


def replace_exact(relative: str, old: str, new: str, count: int = 1) -> None:
    path = root / relative
    text = path.read_text(encoding="utf-8")
    if text.count(old) < count:
        raise SystemExit(f"Patch anchor missing in {relative}: {old[:120]!r}")
    path.write_text(text.replace(old, new, count), encoding="utf-8")


replace_exact(
    ".github/workflows/release-windows.yml",
    '''          $request = $body | ConvertTo-Json -Compress
          $request | gh api --method PUT $apiPath --input - *> $null
          if ($LASTEXITCODE -ne 0) { throw "Failed to record verified release publication." }''',
    '''          $request = $body | ConvertTo-Json -Compress
          $requestPath = Join-Path $env:RUNNER_TEMP "arsas-published-release-request.json"
          [System.IO.File]::WriteAllText(
            $requestPath,
            $request,
            [System.Text.UTF8Encoding]::new($false))
          gh api --method PUT $apiPath --input $requestPath *> $null
          if ($LASTEXITCODE -ne 0) { throw "Failed to record verified release publication." }''',
)

replace_exact(
    ".github/workflows/sync-release-documentation.yml",
    '''          mkdir -p _release-sync
          gh api "repos/$GITHUB_REPOSITORY/releases/tags/$RELEASE_TAG" > _release-sync/release.json
          gh release download "$RELEASE_TAG" \\
            --repo "$GITHUB_REPOSITORY" \\
            --dir _release-sync \\
            --pattern 'ARSAS-Windows-x64-SHA256SUMS.txt' \\
            --clobber
          git fetch --tags --force''',
    '''          mkdir -p _release-sync
          release_ready=false
          for attempt in $(seq 1 30); do
            if gh api "repos/$GITHUB_REPOSITORY/releases/tags/$RELEASE_TAG" > _release-sync/release.tmp.json 2>/dev/null; then
              mv _release-sync/release.tmp.json _release-sync/release.json
              release_ready=true
              break
            fi
            echo "Release $RELEASE_TAG is not visible yet (attempt $attempt/30); retrying in 10 seconds."
            sleep 10
          done
          if [[ "$release_ready" != "true" ]]; then
            echo "Published release $RELEASE_TAG did not become visible within the synchronization window." >&2
            exit 1
          fi

          checksum_ready=false
          for attempt in $(seq 1 12); do
            if gh release download "$RELEASE_TAG" \\
              --repo "$GITHUB_REPOSITORY" \\
              --dir _release-sync \\
              --pattern 'ARSAS-Windows-x64-SHA256SUMS.txt' \\
              --clobber; then
              checksum_ready=true
              break
            fi
            echo "Checksum asset is not downloadable yet (attempt $attempt/12); retrying in 10 seconds."
            sleep 10
          done
          if [[ "$checksum_ready" != "true" ]]; then
            echo "Checksum asset for $RELEASE_TAG did not become downloadable." >&2
            exit 1
          fi
          git fetch --tags --force''',
)

replace_exact(
    ".github/workflows/publish-verified-release.yml",
    '''      - ".release/publish-verified.json"
      - "landing/release-notes.json"
      - ".github/workflows/publish-verified-release.yml"''',
    '''      - ".release/publish-verified.json"
      - ".github/workflows/publish-verified-release.yml"''',
)

print("Hardened release publication JSON, release-evidence synchronization retries, and legacy workflow triggers.")
