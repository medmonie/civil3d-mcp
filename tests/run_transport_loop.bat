@echo off
rem MCP transport test (loop), docs\AUDIT_TEST_PLAN.md A-lines. See tests\transport_live.py. Output: tests\results\transport_loop.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0transport_live.py" loop > "%~dp0results\transport_loop.txt" 2>&1
