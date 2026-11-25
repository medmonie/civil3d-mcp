# Adds (or updates) the civil3d MCP server in the MCP clients found for this Windows user. Called by install.bat.
#   Claude Desktop: claude_desktop_config.json, both the classic install (%APPDATA%\Claude) and the Microsoft Store /
#                   MSIX install (%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude). Other servers and
#                   settings are kept; only mcpServers.civil3d is set.
#   Codex:          ~/.codex/config.toml, the command of its [mcp_servers.civil3d] table.
# Each changed file is backed up first (<file>.bak-<date>). A file that cannot be read safely (not valid JSON) is left
# unchanged and reported, so the user adds the snippet by hand. ChatGPT desktop is configured in its own settings.
# -ClaudeConfigs / -CodexConfig override the locations (used by tests\run_configure_clients.bat).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string[]]$ClaudeConfigs,
    [string]$CodexConfig
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$results = New-Object System.Collections.Generic.List[string]

function Backup([string]$path) {
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination "$path.bak-$stamp" -Force }
}

# ---------------------------------------------------------------- Claude Desktop
if (-not $ClaudeConfigs) {
    $ClaudeConfigs = @()
    $classic = Join-Path $env:APPDATA 'Claude'
    if (Test-Path -LiteralPath $classic) { $ClaudeConfigs += (Join-Path $classic 'claude_desktop_config.json') }
    $pkgs = Join-Path $env:LOCALAPPDATA 'Packages'
    if (Test-Path -LiteralPath $pkgs) {
        foreach ($p in Get-ChildItem -LiteralPath $pkgs -Directory -Filter 'Claude_*' -ErrorAction SilentlyContinue) {
            $d = Join-Path $p.FullName 'LocalCache\Roaming\Claude'
            if (Test-Path -LiteralPath $d) { $ClaudeConfigs += (Join-Path $d 'claude_desktop_config.json') }
        }
    }
}
if ($ClaudeConfigs.Count -eq 0) {
    $results.Add('Claude Desktop: not found for this user (install it, then run install.bat again, or add the snippet by hand).')
}
foreach ($cfg in $ClaudeConfigs) {
    try {
        $text = ''
        if (Test-Path -LiteralPath $cfg) { $text = [IO.File]::ReadAllText($cfg) }
        if ([string]::IsNullOrWhiteSpace($text)) { $obj = New-Object PSObject }
        else {
            try { $obj = $text | ConvertFrom-Json }
            catch { $results.Add("Claude Desktop: NOT CHANGED - $cfg is not valid JSON ($($_.Exception.Message)). Fix it, or add the snippet by hand."); continue }
            if ($obj -isnot [System.Management.Automation.PSCustomObject]) {
                $results.Add("Claude Desktop: NOT CHANGED - $cfg does not hold a JSON object. Add the snippet by hand."); continue
            }
        }
        if ($obj.PSObject.Properties.Name -notcontains 'mcpServers' -or $null -eq $obj.mcpServers) {
            $obj | Add-Member -NotePropertyName mcpServers -NotePropertyValue (New-Object PSObject) -Force
        }
        $existing = if ($obj.mcpServers.PSObject.Properties.Name -contains 'civil3d') { $obj.mcpServers.civil3d } else { $null }
        $before = if ($existing -is [System.Management.Automation.PSCustomObject]) { $existing.command } else { $null }
        if ($before -eq $Exe) { $results.Add("Claude Desktop: already set in $cfg"); continue }
        if ($existing -is [System.Management.Automation.PSCustomObject]) {
            # only the command changes: args (e.g. --read-only), env and other fields of the entry are kept
            $existing | Add-Member -NotePropertyName command -NotePropertyValue $Exe -Force
        }
        else {
            $server = New-Object PSObject
            $server | Add-Member -NotePropertyName command -NotePropertyValue $Exe
            $obj.mcpServers | Add-Member -NotePropertyName civil3d -NotePropertyValue $server -Force
        }
        Backup $cfg
        $dir = Split-Path -Parent $cfg
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        [IO.File]::WriteAllText($cfg, ($obj | ConvertTo-Json -Depth 64), $utf8)
        # read it back: the file must still be valid JSON with the entry
        $check = [IO.File]::ReadAllText($cfg) | ConvertFrom-Json
        if ($check.mcpServers.civil3d.command -ne $Exe) { throw 'the entry was not found after writing' }
        $verb = if ($before) { 'updated' } else { 'added' }
        $results.Add("Claude Desktop: civil3d $verb in $cfg")
    }
    catch { $results.Add("Claude Desktop: NOT CHANGED - $cfg ($($_.Exception.Message)). Add the snippet by hand.") }
}

# ---------------------------------------------------------------- Codex
if (-not $CodexConfig) {
    $codexDir = Join-Path $env:USERPROFILE '.codex'
    if (Test-Path -LiteralPath $codexDir) { $CodexConfig = Join-Path $codexDir 'config.toml' }
}
if (-not $CodexConfig) { $results.Add('Codex: not found for this user (no .codex folder).') }
else {
    try {
        $lines = @()
        if (Test-Path -LiteralPath $CodexConfig) { $lines = [IO.File]::ReadAllLines($CodexConfig) }
        # Only the command line of [mcp_servers.civil3d] changes: args (e.g. --read-only), env, sub-tables and
        # comments are kept. Without the table, it is appended.
        $kept = New-Object System.Collections.Generic.List[string]
        $inSection = $false; $had = $false; $setCommand = $false
        $cmdLine = 'command = ' + (ConvertTo-Json -InputObject $Exe -Compress)
        foreach ($l in $lines) {
            $t = $l.Trim()
            if ($t -match '^\[\[?\s*([^\]]+?)\s*\]\]?\s*(#.*)?$') {
                if ($inSection -and -not $setCommand) { $kept.Add($cmdLine); $setCommand = $true }
                $inSection = (($Matches[1] -replace '"', '') -eq 'mcp_servers.civil3d')
                if ($inSection) { $had = $true }
                $kept.Add($l); continue
            }
            if ($inSection -and $t -match '^command\s*=') {
                if (-not $setCommand) { $kept.Add($cmdLine); $setCommand = $true }
                continue
            }
            $kept.Add($l)
        }
        if ($inSection -and -not $setCommand) { $kept.Add($cmdLine); $setCommand = $true }
        if (-not $had) {
            while ($kept.Count -gt 0 -and [string]::IsNullOrWhiteSpace($kept[$kept.Count - 1])) { $kept.RemoveAt($kept.Count - 1) }
            if ($kept.Count -gt 0) { $kept.Add('') }
            $kept.Add('[mcp_servers.civil3d]')
            $kept.Add($cmdLine)
        }
        if ((($lines -join "`n").TrimEnd()) -eq (($kept -join "`n").TrimEnd())) { $results.Add("Codex: already set in $CodexConfig") }
        else {
            Backup $CodexConfig
            [IO.File]::WriteAllLines($CodexConfig, [string[]]$kept, $utf8)
            $verb = if ($had) { 'updated' } else { 'added' }
            $results.Add("Codex: civil3d $verb in $CodexConfig")
        }
    }
    catch { $results.Add("Codex: NOT CHANGED - $CodexConfig ($($_.Exception.Message)). Add the snippet by hand.") }
}

$results | ForEach-Object { Write-Output "  $_" }
