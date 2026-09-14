[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$mcpExecutable = Join-Path $projectRoot "LdDecryptMcp.exe"
$sourceSkill = Join-Path $projectRoot "codex-plugin\skills\green-shield-decryption"
$codexCommand = Get-Command codex -ErrorAction SilentlyContinue

if (-not (Test-Path -LiteralPath $mcpExecutable -PathType Leaf)) {
    throw "LdDecryptMcp.exe was not found. Extract the complete release package first."
}

if (-not (Test-Path -LiteralPath $sourceSkill -PathType Container)) {
    throw "The Codex skill was not found. Keep the codex-plugin directory intact."
}

if ($null -eq $codexCommand) {
    throw "The codex command was not found. Install and sign in to Codex first."
}

$skillRoot = Join-Path $env:USERPROFILE ".codex\skills"
$targetSkill = Join-Path $skillRoot "green-shield-decryption"
New-Item -ItemType Directory -Path $targetSkill -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceSkill "SKILL.md") -Destination (Join-Path $targetSkill "SKILL.md") -Force

$mcpListJson = & $codexCommand.Source mcp list --json
if ($LASTEXITCODE -ne 0) {
    throw "The existing Codex MCP configuration could not be read."
}

$mcpNames = @($mcpListJson | ConvertFrom-Json | ForEach-Object { $_.name })
if ($mcpNames -contains "green-shield-decryption") {
    & $codexCommand.Source mcp remove green-shield-decryption
    if ($LASTEXITCODE -ne 0) {
        throw "The existing green-shield-decryption MCP configuration could not be updated."
    }
}

& $codexCommand.Source mcp add green-shield-decryption -- $mcpExecutable
if ($LASTEXITCODE -ne 0) {
    throw "Codex MCP registration failed."
}

Write-Host ""
Write-Host "Green Shield MCP and skill are now registered with Codex." -ForegroundColor Green
Write-Host "Keep this folder in place and start a new Codex task to load the integration."
