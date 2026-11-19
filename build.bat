@echo off
rem Builds the plugin and deploys it to the Civil 3D autoload bundle. Log: build_result.txt
rem Close Civil 3D first, or restart it afterwards: a running Civil 3D keeps using the DLL it loaded.
cd /d "%~dp0plugin"
set "D=%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle\Contents\2027"
rem A running Civil 3D locks the DLL against overwrite/delete but not against rename, so move it aside first.
del "%D%\C3dMcpBridge.dll.old*" 2>nul
if exist "%D%\C3dMcpBridge.dll" ren "%D%\C3dMcpBridge.dll" "C3dMcpBridge.dll.old%RANDOM%" 2>nul
rem --no-incremental: never trust file times (sources may be copied from another machine).
dotnet build -c Release --no-incremental > "%~dp0build_result.txt" 2>&1
echo EXIT=%ERRORLEVEL% >> "%~dp0build_result.txt"
dir "%D%" >> "%~dp0build_result.txt" 2>&1
tasklist /fi "imagename eq acad.exe" /fo csv /nh >> "%~dp0build_result.txt" 2>&1
