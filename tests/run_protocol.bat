@echo off
rem MCP protocol checks over stdio (ChatGPT protocol audit), live, in a FRESH Civil 3D with Drawing1.dwg empty and MCPALLOW on. Output: tests\results\protocol.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0live_protocol_test.py" --edits > "%~dp0results\protocol.txt" 2>&1
echo exit code %ERRORLEVEL% >> "%~dp0results\protocol.txt"
