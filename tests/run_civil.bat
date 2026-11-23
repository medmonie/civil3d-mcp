@echo off
rem Civil 3D 0.15 features, live, in a NEW unsaved drawing (Drawing1 after MCPALLOW). Output: tests\results\civil.txt
if not exist "%~dp0results" mkdir "%~dp0results"
"%~dp0..\server\.venv\Scripts\python" "%~dp0civil_live.py" > "%~dp0results\civil.txt" 2>&1
