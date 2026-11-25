@echo off
rem MCP transport test (down), docs\AUDIT_TEST_PLAN.md A-lines. See tests\transport_live.py. Output: tests\results\transport_down.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0transport_live.py" down > "%~dp0results\transport_down.txt" 2>&1
