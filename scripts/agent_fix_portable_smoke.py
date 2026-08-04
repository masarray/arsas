from pathlib import Path

root = Path(__file__).resolve().parents[1]


def replace_exact(path: str, old: str, new: str) -> None:
    target = root / path
    text = target.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"Patch anchor missing in {path}: {old[:120]!r}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_exact(
    "App.xaml.cs",
    '''            if (!File.Exists(lockPath))
                return 21;''',
    '''            if (!File.Exists(lockPath))
            {
                WritePortableSmokeDiagnostic($"Engine lock was not extracted. BaseDirectory={AppContext.BaseDirectory}; expected={lockPath}");
                return 21;
            }''',
)
replace_exact(
    "App.xaml.cs",
    '''        catch
        {
            return 22;
        }
    }

    protected override void OnExit''',
    '''        catch (Exception ex)
        {
            WritePortableSmokeDiagnostic(ex.ToString());
            return 22;
        }
    }

    private static void WritePortableSmokeDiagnostic(string message)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "ARSAS-portable-smoke-error.txt"),
                message);
        }
        catch
        {
            // Diagnostics must never replace the original smoke-test result.
        }
    }

    protected override void OnExit''',
)

old_build = '''          $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:RUNNER_TEMP "ARSAS-bundle-cache"
          & $exe --portable-smoke-test
          if ($LASTEXITCODE -ne 0) { throw "Portable single EXE smoke test failed with exit code $LASTEXITCODE." }'''
new_build = '''          $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:RUNNER_TEMP "ARSAS-bundle-cache"
          $diagnostic = Join-Path ([System.IO.Path]::GetTempPath()) "ARSAS-portable-smoke-error.txt"
          Remove-Item $diagnostic -Force -ErrorAction SilentlyContinue
          $process = Start-Process -FilePath $exe -ArgumentList @("--portable-smoke-test") -PassThru
          if (-not $process.WaitForExit(30000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Portable single EXE smoke test timed out."
          }
          if ($process.ExitCode -ne 0) {
            if (Test-Path $diagnostic) { Get-Content $diagnostic | Write-Host }
            throw "Portable single EXE smoke test failed with exit code $($process.ExitCode)."
          }'''
replace_exact(".github/workflows/build.yml", old_build, new_build)

old_release = '''          $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:RUNNER_TEMP "ARSAS-release-bundle-cache"
          & $exe --portable-smoke-test
          if ($LASTEXITCODE -ne 0) { throw "Portable single EXE smoke test failed with exit code $LASTEXITCODE." }'''
new_release = '''          $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $env:RUNNER_TEMP "ARSAS-release-bundle-cache"
          $diagnostic = Join-Path ([System.IO.Path]::GetTempPath()) "ARSAS-portable-smoke-error.txt"
          Remove-Item $diagnostic -Force -ErrorAction SilentlyContinue
          $process = Start-Process -FilePath $exe -ArgumentList @("--portable-smoke-test") -PassThru
          if (-not $process.WaitForExit(30000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Portable single EXE smoke test timed out."
          }
          if ($process.ExitCode -ne 0) {
            if (Test-Path $diagnostic) { Get-Content $diagnostic | Write-Host }
            throw "Portable single EXE smoke test failed with exit code $($process.ExitCode)."
          }'''
replace_exact(".github/workflows/release-windows.yml", old_release, new_release)
