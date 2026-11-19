@echo off
rem Builds a self-contained distributable: dist\C3dMcp-2027.zip
rem Needs on THIS machine only: .NET 10 SDK, Python 3.10+ with server\.venv (internet for pip once).
cd /d "%~dp0"
set LOG=%~dp0package_build.txt
echo build started > "%LOG%"
if exist dist rmdir /s /q dist
mkdir dist\C3dMcp\bundle\C3dMcpBridge.bundle\Contents\2027

echo [1/3] plugin >> "%LOG%"
dotnet build plugin -c Release -p:DeployBundle=false >> "%LOG%" 2>&1 || goto fail
copy /y plugin\PackageContents.xml dist\C3dMcp\bundle\C3dMcpBridge.bundle\ >> "%LOG%" 2>&1
copy /y plugin\bin\Release\net10.0-windows\C3dMcpBridge.dll dist\C3dMcp\bundle\C3dMcpBridge.bundle\Contents\2027\ >> "%LOG%" 2>&1 || goto fail

echo [2/3] server exe >> "%LOG%"
server\.venv\Scripts\python -m pip install -r server\requirements.txt pyinstaller >> "%LOG%" 2>&1 || goto fail
server\.venv\Scripts\python -m PyInstaller --onefile --noconfirm --name c3d-mcp-server --collect-all mcp --paths server --hidden-import catalog --hidden-import qa --hidden-import bridge_client --hidden-import sewergems --collect-submodules openpyxl --distpath dist\C3dMcp --workpath dist\_work --specpath dist\_work server\server.py >> "%LOG%" 2>&1 || goto fail

echo [3/3] package >> "%LOG%"
copy /y package\install.bat dist\C3dMcp\ >> "%LOG%" 2>&1
copy /y package\uninstall.bat dist\C3dMcp\ >> "%LOG%" 2>&1
copy /y package\INSTALL.txt dist\C3dMcp\ >> "%LOG%" 2>&1
copy /y README.md dist\C3dMcp\ >> "%LOG%" 2>&1
powershell -NoProfile -Command "Compress-Archive -Path dist\C3dMcp -DestinationPath dist\C3dMcp-2027.zip -Force" >> "%LOG%" 2>&1 || goto fail
echo RESULT=OK >> "%LOG%"
exit /b 0
:fail
echo RESULT=FAILED >> "%LOG%"
exit /b 1
