@echo off
set "APP=%~dp0LdDecryptHotkey.exe"
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws=New-Object -ComObject WScript.Shell; $s=$ws.CreateShortcut('%STARTUP%\\GreenShieldQuickApply.lnk'); $s.TargetPath='%APP%'; $s.WorkingDirectory='%~dp0'; $s.Save()"
echo Done. GreenShieldQuickApply will start when Windows starts.
pause
