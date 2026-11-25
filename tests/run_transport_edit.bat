@echo off
rem MCP transport test (edit), docs\AUDIT_TEST_PLAN.md A-lines. See tests\transport_live.py. Output: tests\results\transport_edit.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0transport_live.py" edit > "%~dp0results\transport_edit.txt" 2>&1
