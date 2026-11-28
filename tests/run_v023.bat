@echo off
rem 0.23 (coordinate system reads and set, file policy, strict whole numbers, permission retry), live, in a NEW unsaved metric drawing (DrawingN after MCPALLOW). Output: tests\results\v023.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0v023_live.py" > "%~dp0results\v023.txt" 2>&1
