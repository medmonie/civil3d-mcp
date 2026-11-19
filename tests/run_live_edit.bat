@echo off
rem Runs the edit tests in the ACTIVE drawing (scratch drawings only). Output: tests\results\live_edit.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0live_edit.py" %* > "%~dp0results\live_edit.txt" 2>&1
