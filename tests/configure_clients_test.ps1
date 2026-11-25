# Offline test of package\configure_clients.ps1 against sample files in %TEMP% (never the real client configs).
# Run tests\run_configure_clients.bat; output: tests\results\configure_clients.txt
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot '..\package\configure_clients.ps1'
$dir = Join-Path $env:TEMP ('mcp_cfg_' + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Path $dir | Out-Null
$exe = 'C:\Users\Some One\AppData\Local\C3dMcpBridge\c3d-mcp-server.exe'
$a = Join-Path $dir 'a_other_servers.json'; $b = Join-Path $dir 'b_empty.json'; $c = Join-Path $dir 'c_invalid.json'
$d = Join-Path $dir 'd_missing.json'; $e = Join-Path $dir 'e_old_path.json'; $t = Join-Path $dir 'config.toml'
[IO.File]::WriteAllText($a, '{"mcpServers":{"other":{"command":"x.exe","args":["--flag"],"env":{"K":"V"}}},"globalShortcut":"Ctrl+Space","list":["one"]}')
[IO.File]::WriteAllText($b, '')
[IO.File]::WriteAllText($c, '{ "mcpServers": { "other": { "command": "x.exe", } ')
[IO.File]::WriteAllText($e, '{"mcpServers":{"civil3d":{"command":"C:\\old\\c3d-mcp-server.exe","args":["--read-only"],"env":{"X":"1"}}}}')
[IO.File]::WriteAllText($t, "model = `"o3`"`n`n[mcp_servers.civil3d]`ncommand = `"C:\\old.exe`"`nargs = [`"--read-only`"]`n`n[mcp_servers.civil3d.env]`nX = `"1`"`n`n[mcp_servers.other]`ncommand = `"y.exe`"`n")

$fail = 0
function Check($name, $ok, $detail) { if ($ok) { "PASS  $name" } else { $script:fail++; "FAIL  $name  $detail" } }

"===== first run"
& $script -Exe $exe -ClaudeConfigs @($a, $b, $c, $d, $e) -CodexConfig $t
"===== second run (nothing should change)"
& $script -Exe $exe -ClaudeConfigs @($a, $b, $c, $d, $e) -CodexConfig $t

foreach ($f in @($a, $b, $d, $e)) { "----- $f"; Get-Content -Raw $f }
"----- $t"; Get-Content -Raw $t

$ja = Get-Content -Raw $a | ConvertFrom-Json
Check 'a: civil3d added' ($ja.mcpServers.civil3d.command -eq $exe) ''
Check 'a: other server kept with args and env' ($ja.mcpServers.other.command -eq 'x.exe' -and $ja.mcpServers.other.args[0] -eq '--flag' -and $ja.mcpServers.other.env.K -eq 'V') ''
Check 'a: other settings kept (shortcut, one-item list)' ($ja.globalShortcut -eq 'Ctrl+Space' -and @($ja.list).Count -eq 1 -and @($ja.list)[0] -eq 'one') ''
Check 'b: empty file gets the entry' ((Get-Content -Raw $b | ConvertFrom-Json).mcpServers.civil3d.command -eq $exe) ''
Check 'c: invalid JSON left unchanged' ((Get-Content -Raw $c) -eq '{ "mcpServers": { "other": { "command": "x.exe", } ') ''
Check 'd: missing file created' ((Get-Content -Raw $d | ConvertFrom-Json).mcpServers.civil3d.command -eq $exe) ''
$je = Get-Content -Raw $e | ConvertFrom-Json
Check 'e: old path updated, args --read-only and env kept' ($je.mcpServers.civil3d.command -eq $exe -and $je.mcpServers.civil3d.args[0] -eq '--read-only' -and $je.mcpServers.civil3d.env.X -eq '1') ''
Check 'backups written once (a, e)' (@(Get-ChildItem $dir -Filter 'a_other_servers.json.bak-*').Count -eq 1 -and @(Get-ChildItem $dir -Filter 'e_old_path.json.bak-*').Count -eq 1) (Get-ChildItem $dir | Select-Object -Expand Name)
Check 'codex: second run left it alone (one backup)' (@(Get-ChildItem $dir -Filter 'config.toml.bak-*').Count -eq 1) (Get-ChildItem $dir | Select-Object -Expand Name)
$toml = Get-Content -Raw $t
Check 'codex: one civil3d section with the new path' (([regex]::Matches($toml, '\[mcp_servers\.civil3d\]')).Count -eq 1 -and $toml.Contains('c3d-mcp-server.exe') -and -not $toml.Contains('old.exe')) ''
Check 'codex: args, civil3d.env sub-table, other server and model kept' ($toml.Contains('args = ["--read-only"]') -and $toml.Contains('[mcp_servers.civil3d.env]') -and $toml.Contains('[mcp_servers.other]') -and $toml.Contains('model = "o3"')) ''
if ($fail -eq 0) { 'ALL PASS' } else { "FAILED ($fail)" }
