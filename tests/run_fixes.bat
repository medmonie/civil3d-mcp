@echo off
rem Fixes after the audit test (D4-D8), live, in a NEW unsaved metric drawing (DrawingN after MCPALLOW). Output: tests\results\fixes.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0fixes_live.py" > "%~dp0results\fixes.txt" 2>&1
