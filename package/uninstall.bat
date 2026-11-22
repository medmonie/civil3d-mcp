@echo off
rmdir /s /q "%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle"
rmdir /s /q "%LOCALAPPDATA%\C3dMcpBridge"
echo Remove the civil3d entry from ChatGPT desktop Settings ^> MCP servers or from the relevant client config.
echo If you set up a ChatGPT tunnel, stop its runtime and remove the custom MCP server from ChatGPT.
pause
