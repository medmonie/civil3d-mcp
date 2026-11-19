@echo off
setlocal
cd /d "%~dp0"
echo Closing Civil 3D is required if it is running (the plugin DLL gets replaced).
tasklist /fi "imagename eq acad.exe" | find /i "acad.exe" >nul && (echo Please close Civil 3D and run this again. & pause & exit /b 1)

set BUNDLE=%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle
set HOME_DIR=%LOCALAPPDATA%\C3dMcpBridge
xcopy /e /i /y "bundle\C3dMcpBridge.bundle" "%BUNDLE%" >nul || (echo Plugin copy failed & pause & exit /b 1)
if not exist "%HOME_DIR%" mkdir "%HOME_DIR%"
copy /y "c3d-mcp-server.exe" "%HOME_DIR%\" >nul || (echo Server copy failed & pause & exit /b 1)

set SNIP=%HOME_DIR%\claude_desktop_config_snippet.json
> "%SNIP%" echo {
>> "%SNIP%" echo   "mcpServers": {
>> "%SNIP%" echo     "civil3d": { "command": "%HOME_DIR:\=\\%\\c3d-mcp-server.exe" }
>> "%SNIP%" echo   }
>> "%SNIP%" echo }
echo.
echo Installed. Plugin: %BUNDLE%
echo Server:  %HOME_DIR%\c3d-mcp-server.exe
echo.
echo Next: Claude Desktop ^> Settings ^> Developer ^> Edit Config, add the block from:
echo   %SNIP%
echo then restart Claude Desktop and Civil 3D 2027. (Civil 3D may ask once to trust the plugin: choose Always Load.)
type "%SNIP%"
pause
