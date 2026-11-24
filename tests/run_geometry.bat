@echo off
rem 0.17 geometry, quantities and labels, live, in a NEW unsaved drawing (Drawing1 after MCPALLOW). Output: tests\results\geometry.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0geometry_live.py" > "%~dp0results\geometry.txt" 2>&1
