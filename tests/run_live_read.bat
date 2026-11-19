@echo off
rem Runs every read action against the live Civil 3D. Output: tests\results\live_read.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0live_read.py" > "%~dp0results\live_read.txt" 2>&1
