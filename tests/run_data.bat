@echo off
rem 0.16 data reads, live, in a NEW unsaved drawing (Drawing1 after MCPALLOW). Output: tests\results\data.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0data_live.py" > "%~dp0results\data.txt" 2>&1
