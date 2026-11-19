@echo off
rmdir /s /q "%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle"
rmdir /s /q "%LOCALAPPDATA%\C3dMcpBridge"
echo Removed. Also delete the "civil3d" entry from Claude Desktop's config.
pause
