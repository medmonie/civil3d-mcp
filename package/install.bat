@echo off
setlocal
cd /d "%~dp0"
echo Closing Civil 3D is required if it is running (the plugin DLL gets replaced).
tasklist /fi "imagename eq acad.exe" | find /i "acad.exe" >nul && (echo Please close Civil 3D and run this again. & pause & exit /b 1)
rem Per-user install: %APPDATA% and %LOCALAPPDATA% belong to the account this window runs as. "Run as administrator"
rem with another account's credentials (common on servers and domain controllers) would install for that account.
set "C3D_DESKTOP_USER="
for /f "usebackq delims=" %%U in (`powershell -NoProfile -Command "$s=(Get-Process -Id $PID).SessionId; $p=Get-CimInstance Win32_Process -Filter ('Name=''explorer.exe'' AND SessionId=' + $s) | Select-Object -First 1; if ($p) { (Invoke-CimMethod -InputObject $p -MethodName GetOwner).User }"`) do set "C3D_DESKTOP_USER=%%U"
if not defined C3D_DESKTOP_USER goto user_ok
if /i "%C3D_DESKTOP_USER%"=="%USERNAME%" goto user_ok
echo.
echo This window runs as %USERDOMAIN%\%USERNAME%, but the signed-in desktop user is %C3D_DESKTOP_USER%.
echo The files would go to %USERNAME%'s profile, not %C3D_DESKTOP_USER%'s.
echo Run this file by double-clicking it, without "Run as administrator" - no admin rights are needed.
echo To set it up for another person, sign in to Windows as that person and run it there.
pause
exit /b 1
:user_ok
echo Installing for %USERDOMAIN%\%USERNAME% (%APPDATA% and %LOCALAPPDATA%).

set "BUNDLE=%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle"
set "C3D_INSTALL_DIR=%LOCALAPPDATA%\C3dMcpBridge"
xcopy /e /i /y "bundle\C3dMcpBridge.bundle" "%BUNDLE%" >nul || (echo Plugin copy failed & pause & exit /b 1)
if not exist "%C3D_INSTALL_DIR%" mkdir "%C3D_INSTALL_DIR%"
copy /y "c3d-mcp-server.exe" "%C3D_INSTALL_DIR%\" >nul || (echo Server copy failed & pause & exit /b 1)

set "C3D_CODEX_SNIP=%C3D_INSTALL_DIR%\codex_mcp_config_snippet.toml"
powershell -NoProfile -Command "$ErrorActionPreference = 'Stop'; $exe = Join-Path $env:C3D_INSTALL_DIR 'c3d-mcp-server.exe'; $command = ConvertTo-Json -InputObject $exe -Compress; [IO.File]::WriteAllLines($env:C3D_CODEX_SNIP, [string[]]@('[mcp_servers.civil3d]', ('command = ' + $command)), [Text.UTF8Encoding]::new($false))" || (echo Codex configuration snippet could not be written & pause & exit /b 1)

set "SNIP=%C3D_INSTALL_DIR%\claude_desktop_config_snippet.json"
> "%SNIP%" echo {
>> "%SNIP%" echo   "mcpServers": {
>> "%SNIP%" echo     "civil3d": { "command": "%C3D_INSTALL_DIR:\=\\%\\c3d-mcp-server.exe" }
>> "%SNIP%" echo   }
>> "%SNIP%" echo }
echo.
echo Installed. Plugin: %BUNDLE%
echo Server:  "%C3D_INSTALL_DIR%\c3d-mcp-server.exe"
echo.
echo MCP clients on this PC (the civil3d entry is added or updated; other servers are kept; each file is backed up first):
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0configure_clients.ps1" -Exe "%C3D_INSTALL_DIR%\c3d-mcp-server.exe"
if errorlevel 1 echo   The MCP clients could not be configured automatically: add the snippets below by hand.
echo.
echo   Claude Desktop / Codex: fully quit and restart them to load the server.
echo   If a client above says NOT CHANGED or not found, add the block by hand:
echo     Claude Desktop: Settings ^> Developer ^> Edit Config, merge "%SNIP%" into mcpServers
echo     Codex: add "%C3D_CODEX_SNIP%" to ~/.codex/config.toml
echo   ChatGPT desktop: Settings ^> MCP servers ^> Add server, name civil3d, STDIO,
echo                   command = the server path above. Save and restart as prompted.
echo.
echo Then open a drawing in Civil 3D 2024, 2025 or 2027 and run MCPSTATUS in the command line.
pause
