@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-windows-portable.ps1" %*
if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)
echo.
echo Build complete. See the dist folder.
endlocal
