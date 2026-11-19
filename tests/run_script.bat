@echo off
rem Development aid: runs tests\script.py against the live Civil 3D. Output: tests\results\script.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0script.py" > "%~dp0results\script.txt" 2>&1
