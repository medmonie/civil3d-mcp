@echo off
rem Builds the plugin for every Civil 3D release whose DLLs are found (2024 -> .NET Framework 4.8, 2025 -> .NET 8,
rem 2027 -> .NET 10; installed, or reference copies in refs\YYYY\) and deploys each to the autoload bundle.
rem Log: build_result.txt. Close Civil 3D first, or restart it afterwards: a running Civil 3D keeps the DLL it loaded.
cd /d "%~dp0plugin"
set "B=%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle\Contents"
rem A running Civil 3D locks the DLL against overwrite/delete but not against rename, so move it aside first.
for %%R in (2024 2025 2027) do (
  del "%B%\%%R\C3dMcpBridge.dll.old*" 2>nul
  if exist "%B%\%%R\C3dMcpBridge.dll" ren "%B%\%%R\C3dMcpBridge.dll" "C3dMcpBridge.dll.old%RANDOM%" 2>nul
)
rem --no-incremental: never trust file times (sources may be copied from another machine).
dotnet build -c Release --no-incremental > "%~dp0build_result.txt" 2>&1
echo EXIT=%ERRORLEVEL% >> "%~dp0build_result.txt"
for %%R in (2024 2025 2027) do if exist "%B%\%%R" dir "%B%\%%R" >> "%~dp0build_result.txt" 2>&1
tasklist /fi "imagename eq acad.exe" /fo csv /nh >> "%~dp0build_result.txt" 2>&1
