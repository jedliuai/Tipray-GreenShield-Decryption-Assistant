@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo C# compiler not found: %CSC%
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:x64 /out:"%~dp0LdDecryptHotkey.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll" /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll" /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll" "%~dp0LdDecryptHotkey.cs"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:exe /define:CLI /platform:x64 /out:"%~dp0LdDecryptHotkeyCli.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll" /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll" /reference:"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll" "%~dp0LdDecryptHotkey.cs"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:exe /platform:x64 /out:"%~dp0LdDecryptMcp.exe" /reference:System.Web.Extensions.dll "%~dp0LdDecryptMcp.cs"
if errorlevel 1 exit /b 1
if not exist "%~dp0codex-plugin\bin" mkdir "%~dp0codex-plugin\bin"
copy /y "%~dp0LdDecryptHotkey.exe" "%~dp0codex-plugin\bin\LdDecryptHotkey.exe" >nul
if errorlevel 1 exit /b 1
copy /y "%~dp0LdDecryptMcp.exe" "%~dp0codex-plugin\bin\LdDecryptMcp.exe" >nul
if errorlevel 1 exit /b 1
echo Build completed successfully.
