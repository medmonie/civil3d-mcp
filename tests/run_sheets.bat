@echo off
rem Sheets and AutoCAD set-up live test on a copy of the open drawing. Output: tests\results\sheets.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0sheets_live.py" > "%~dp0results\sheets.txt" 2>&1
