param(
    [string]$ConfigPath = "$env:USERPROFILE\.openclaw\openclaw.json"
)

$ErrorActionPreference = "Stop"

# Resolve repository root from this script location.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$societySource = Join-Path $repoRoot "OpenClaw_Agents\society"

if (-not (Test-Path $ConfigPath)) {
    throw "OpenClaw config not found: $ConfigPath"
}

if (-not (Test-Path $societySource)) {
    throw "Society agent source folder not found: $societySource"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = "$ConfigPath.backup-before-society-agent-$timestamp"

Copy-Item $ConfigPath $backupPath -Force
Write-Host "Backup created at: $backupPath"

$societyWorkspace = "$env:USERPROFILE\.openclaw\workspace-society"
$societyAgentDir = "$env:USERPROFILE\.openclaw\agents\society\agent"

New-Item -ItemType Directory -Force -Path $societyWorkspace | Out-Null
New-Item -ItemType Directory -Force -Path $societyAgentDir | Out-Null

Copy-Item (Join-Path $societySource "*.md") $societyAgentDir -Force

$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json

if (-not $config.PSObject.Properties.Name.Contains("agents")) {
    throw "Invalid OpenClaw config: missing agents section."
}

if (-not $config.agents.PSObject.Properties.Name.Contains("list")) {
    throw "Invalid OpenClaw config: missing agents.list section."
}

$agents = @($config.agents.list)
$existing = $agents | Where-Object { $_.id -eq "society" } | Select-Object -First 1

if ($null -eq $existing) {
    Write-Host "Adding society agent to OpenClaw config."

    $societyEntry = [pscustomobject][ordered]@{
        id = "society"
        name = "society"
        workspace = $societyWorkspace
        agentDir = $societyAgentDir
        model = "ollama-npc-qwen3/qwen3:4b-instruct"
        identity = [pscustomobject][ordered]@{
            name = "Society Scheduler"
            theme = "Autonomous society-level scheduler agent"
        }
        skills = @()
    }

    $agents += $societyEntry
    $config.agents.list = $agents
}
else {
    Write-Host "Updating existing society agent in OpenClaw config."

    $existing.name = "society"
    $existing.workspace = $societyWorkspace
    $existing.agentDir = $societyAgentDir
    $existing.model = "ollama-npc-qwen3/qwen3:4b-instruct"

    if ($null -eq $existing.identity) {
        $existing | Add-Member -MemberType NoteProperty -Name "identity" -Value ([pscustomobject]@{}) -Force
    }

    if ($existing.identity.PSObject.Properties.Name.Contains("name")) {
        $existing.identity.name = "Society Scheduler"
    }
    else {
        $existing.identity | Add-Member -MemberType NoteProperty -Name "name" -Value "Society Scheduler"
    }

    if ($existing.identity.PSObject.Properties.Name.Contains("theme")) {
        $existing.identity.theme = "Autonomous society-level scheduler agent"
    }
    else {
        $existing.identity | Add-Member -MemberType NoteProperty -Name "theme" -Value "Autonomous society-level scheduler agent"
    }

    if (-not $existing.PSObject.Properties.Name.Contains("skills")) {
        $existing | Add-Member -MemberType NoteProperty -Name "skills" -Value @()
    }
}

$config |
    ConvertTo-Json -Depth 50 |
    Set-Content $ConfigPath -Encoding UTF8

Write-Host "Society agent registered successfully."
Write-Host "Agent route: openclaw/society"
Write-Host "Model: ollama-npc-qwen3/qwen3:4b-instruct"
