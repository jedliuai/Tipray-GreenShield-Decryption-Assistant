@echo off
"%~dp0LdDecryptHotkeyCli.exe" --uninstall-startup
if errorlevel 1 echo Failed to disable startup.
pause
