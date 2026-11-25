@echo off
rem Audit fixes C1-C13, A4, A7 and the D records, live, in a NEW unsaved metric drawing (DrawingN after MCPALLOW). Output: tests\results\audit.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0audit_live.py" > "%~dp0results\audit.txt" 2>&1
