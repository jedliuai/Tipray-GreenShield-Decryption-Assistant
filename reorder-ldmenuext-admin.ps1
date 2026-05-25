$ErrorActionPreference = 'Stop'

$clsid = '{4EC0342D-356C-46DF-9088-86E55FDA80C6}'
$backupDir = Join-Path $PSScriptRoot ('registry-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backupDir | Out-Null

reg export "HKCR\*\shellex\ContextMenuHandlers" (Join-Path $backupDir 'HKCR-star-ContextMenuHandlers.reg') /y | Out-Null
reg export "HKCR\Directory\shellex\ContextMenuHandlers" (Join-Path $backupDir 'HKCR-Directory-ContextMenuHandlers.reg') /y | Out-Null

$targets = @(
  @{ Old = 'HKCR\*\shellex\ContextMenuHandlers\LdMenuExt'; New = 'HKCR\*\shellex\ContextMenuHandlers\00_LdMenuExt' },
  @{ Old = 'HKCR\Directory\shellex\ContextMenuHandlers\LdMenuExt'; New = 'HKCR\Directory\shellex\ContextMenuHandlers\00_LdMenuExt' }
)

foreach ($target in $targets) {
  reg add $target.New /ve /d $clsid /f | Out-Null
  reg delete $target.Old /f | Out-Null
}

Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
Start-Process explorer.exe

Write-Host "Done. Backup saved to: $backupDir"
Read-Host "Press Enter to close"
