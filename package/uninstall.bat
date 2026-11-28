@echo off
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
echo Removing the install of %USERDOMAIN%\%USERNAME%.
tasklist /fi "imagename eq claude.exe" | find /i "claude.exe" >nul && (echo Quit Claude Desktop completely ^(tray icon ^> Quit^) first, then run uninstall.bat again. & pause & exit /b 1)
set "TOOL=%LOCALAPPDATA%\C3dMcpBridge\c3d-mcp-server.exe"
if not exist "%TOOL%" set "TOOL=%~dp0c3d-mcp-server.exe"
if exist "%TOOL%" "%TOOL%" --remove-clients
rmdir /s /q "%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle"
rmdir /s /q "%LOCALAPPDATA%\C3dMcpBridge"
echo Claude Desktop and Codex entries removed (files backed up first). Remove civil3d from ChatGPT desktop MCP servers by hand if you added it.
echo If you set up a ChatGPT tunnel, stop its runtime and remove the custom MCP server from ChatGPT.
pause
