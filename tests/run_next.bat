@echo off
rem 0.21 features (#52 show_objects, #15 profile view labels, #16 earthwork by station range), live, in a NEW unsaved metric drawing (DrawingN after MCPALLOW). Output: tests\results\next.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0next_live.py" > "%~dp0results\next.txt" 2>&1
