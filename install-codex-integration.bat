@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-codex-integration.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed. See the message above.
  pause
  exit /b 1
)
pause
