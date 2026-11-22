@echo off
rmdir /s /q "%APPDATA%\Autodesk\ApplicationPlugins\C3dMcpBridge.bundle"
rmdir /s /q "%LOCALAPPDATA%\C3dMcpBridge"
echo Remove the civil3d entry from your MCP client: claude_desktop_config.json, ChatGPT desktop MCP servers, or ~/.codex/config.toml.
echo If you set up a ChatGPT tunnel, stop its runtime and remove the custom MCP server from ChatGPT.
pause
