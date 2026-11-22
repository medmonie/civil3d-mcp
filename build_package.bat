@echo off
rem Builds the distributables in dist\ (log: package_build.txt):
rem   C3dMcp.zip         installer: plugin bundle (Civil 3D 2024 + 2025 + 2027) + c3d-mcp-server.exe + install.bat
rem   C3dMcp-source.zip  clean source tree for building on another PC (no bin, obj, .venv, dist, refs, logs)
rem Needs on THIS machine only: .NET 10 SDK, Python 3.10+ with server\.venv (run setup_server.bat once).
rem Each release is built when its Civil 3D DLLs are found (installed, or copied into refs\YYYY\ - see refs\README.md).
rem A release that cannot be built here is taken from plugin\prebuilt\YYYY\ if present (each package build refreshes
rem that folder for the releases it built). The log says, per release, which source was used.
cd /d "%~dp0"
set LOG=%~dp0package_build.txt
echo build started %DATE% %TIME% > "%LOG%"
if exist dist rmdir /s /q dist
set "OUT=dist\C3dMcp\bundle\C3dMcpBridge.bundle"
mkdir "%OUT%\Contents"

echo [1/4] plugin >> "%LOG%"
if exist plugin\bin\Release rmdir /s /q plugin\bin\Release
dotnet build plugin -c Release --no-incremental -p:DeployBundle=false >> "%LOG%" 2>&1 || goto fail
copy /y plugin\PackageContents.xml "%OUT%\" >> "%LOG%" 2>&1
set FOUND=0
set MISSING=
call :release 2024 net48
call :release 2025 net8.0-windows
call :release 2027 net10.0-windows
if "%FOUND%"=="0" goto fail

echo [2/4] server exe >> "%LOG%"
server\.venv\Scripts\python -m pip install -r server\requirements.txt pyinstaller >> "%LOG%" 2>&1 || goto fail
server\.venv\Scripts\python -m PyInstaller --onefile --noconfirm --name c3d-mcp-server --collect-all mcp --paths server --hidden-import catalog --hidden-import qa --hidden-import bridge_client --hidden-import sewergems --hidden-import watergems --collect-submodules openpyxl --distpath dist\C3dMcp --workpath dist\_work --specpath dist\_work server\server.py >> "%LOG%" 2>&1 || goto fail

echo [3/4] installer zip >> "%LOG%"
copy /y package\install.bat dist\C3dMcp\ >> "%LOG%" 2>&1
copy /y package\uninstall.bat dist\C3dMcp\ >> "%LOG%" 2>&1
copy /y package\INSTALL.txt dist\C3dMcp\ >> "%LOG%" 2>&1
copy /y README.md dist\C3dMcp\ >> "%LOG%" 2>&1
powershell -NoProfile -Command "Compress-Archive -Path dist\C3dMcp -DestinationPath dist\C3dMcp.zip -Force" >> "%LOG%" 2>&1 || goto fail

echo [4/4] source zip >> "%LOG%"
set "SRC=dist\_src\civil3d-mcp"
robocopy . "%SRC%" /E /NFL /NDL /NJH /NJS /XD dist bin obj .venv .venv_old __pycache__ .git .vs results refs prebuilt /XF local.props build_result.txt setup_result.txt package_build.txt archive_result.txt plot.log *.user *.suo *.zip >> "%LOG%" 2>&1
if errorlevel 8 goto fail
mkdir "%SRC%\refs" 2>nul
copy /y refs\README.md "%SRC%\refs\" >> "%LOG%" 2>&1
powershell -NoProfile -Command "Compress-Archive -Path dist\_src\civil3d-mcp -DestinationPath dist\C3dMcp-source.zip -Force" >> "%LOG%" 2>&1 || goto fail
rmdir /s /q dist\_src

if defined MISSING (echo RESULT=OK_PARTIAL missing:%MISSING% >> "%LOG%") else (echo RESULT=OK >> "%LOG%")
exit /b 0
:fail
echo RESULT=FAILED >> "%LOG%"
exit /b 1

rem :release <year> <tfm> - put that release's DLLs in the bundle: fresh build first, else plugin\prebuilt\<year>.
:release
if exist "plugin\bin\Release\%2\C3dMcpBridge.dll" (
  mkdir "%OUT%\Contents\%1" 2>nul
  copy /y "plugin\bin\Release\%2\*.dll" "%OUT%\Contents\%1\" >> "%LOG%" 2>&1
  if exist "plugin\prebuilt\%1" rmdir /s /q "plugin\prebuilt\%1"
  mkdir "plugin\prebuilt\%1" 2>nul
  copy /y "plugin\bin\Release\%2\*.dll" "plugin\prebuilt\%1\" >> "%LOG%" 2>&1
  echo RELEASE %1: built here >> "%LOG%"
  set /a FOUND+=1
  exit /b 0
)
if exist "plugin\prebuilt\%1\C3dMcpBridge.dll" (
  mkdir "%OUT%\Contents\%1" 2>nul
  copy /y "plugin\prebuilt\%1\*.dll" "%OUT%\Contents\%1\" >> "%LOG%" 2>&1
  echo RELEASE %1: NOT built here, using plugin\prebuilt\%1 - check it is from this version >> "%LOG%"
  set /a FOUND+=1
  exit /b 0
)
echo RELEASE %1: MISSING - no Civil 3D %1 DLLs found and no prebuilt DLL >> "%LOG%"
set "MISSING=%MISSING% %1"
exit /b 0
