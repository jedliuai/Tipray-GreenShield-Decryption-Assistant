@echo off
"%~dp0LdDecryptHotkeyCli.exe" --install-startup
if errorlevel 1 echo Failed to enable startup.
pause
