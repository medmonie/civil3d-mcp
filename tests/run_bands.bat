@echo off
rem Profile view band check on the open drawing (dry runs only). Output: tests\results\bands.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0bands_live.py" > "%~dp0results\bands.txt" 2>&1
