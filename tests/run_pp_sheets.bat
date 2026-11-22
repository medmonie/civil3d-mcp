@echo off
rem Plan and profile sheet generator, live, on a drawing named *mcp_test*. Output: tests\results\pp_sheets.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0pp_sheets_live.py" > "%~dp0results\pp_sheets.txt" 2>&1
