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
rmdir /s /q "%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle"
rmdir /s /q "%LOCALAPPDATA%\C3dMcpBridge"
echo Remove the civil3d entry from your MCP client: claude_desktop_config.json, ChatGPT desktop MCP servers, or ~/.codex/config.toml.
echo If you set up a ChatGPT tunnel, stop its runtime and remove the custom MCP server from ChatGPT.
pause
