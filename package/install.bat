@echo off
setlocal
cd /d "%~dp0"
echo Closing Civil 3D is required if it is running (the plugin DLL gets replaced).
tasklist /fi "imagename eq acad.exe" | find /i "acad.exe" >nul && (echo Please close Civil 3D and run this again. & pause & exit /b 1)

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
echo In ChatGPT desktop, open Settings ^> MCP servers ^> Add server.
echo Name it civil3d, choose STDIO and use the server path above as the command.
echo If civil3d already exists, update its command instead. Save and restart as prompted.
echo Config file snippet for Codex: "%C3D_CODEX_SNIP%"
echo Open a drawing in Civil 3D 2027, then run MCPSTATUS in the Civil 3D command line.
echo See README.md for the ChatGPT cloud setup and "%SNIP%" for Claude Desktop.
pause
